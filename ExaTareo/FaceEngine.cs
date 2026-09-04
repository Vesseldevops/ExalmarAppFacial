using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ExaTareo;

public sealed record FaceExtractionResult
{
    private FaceExtractionResult(float[]? vector, string? error) { Vector = vector; Error = error; }
    public float[]? Vector { get; }
    public string? Error { get; }
    public bool IsSuccess => Vector is not null;
    public static FaceExtractionResult Accepted(float[] vector) => new(vector, null);
    public static FaceExtractionResult Retry(string message) => new(null, message);
}

public sealed record FaceExtractionOptions(double MaxAlignmentError, string AlignmentErrorMessage)
{
    public static readonly FaceExtractionOptions Default = new(5,
        "Mira de frente, sin girar ni inclinar demasiado la cabeza, y vuelve a capturar.");
    public static readonly FaceExtractionOptions SideEnrollment = new(14,
        "Gira solo un poco el rostro, mantén ambos ojos visibles y vuelve a capturar.");
    public static readonly FaceExtractionOptions VerticalEnrollment = new(10,
        "Inclina solo un poco el mentón, mantén el rostro visible y vuelve a capturar.");
    public static readonly FaceExtractionOptions GuidedEnrollment = SideEnrollment;

    public static FaceExtractionOptions ForAngle(CaptureAngle angle) => angle switch
    {
        CaptureAngle.Front => Default,
        CaptureAngle.Left or CaptureAngle.Right => SideEnrollment,
        CaptureAngle.Up or CaptureAngle.Down => VerticalEnrollment,
        _ => Default
    };
}

// ARGB pixels. CPU inference; no network, camera or storage dependencies.
public sealed class FaceEngine : IDisposable
{
    public const int VectorSize = 128;
    private readonly InferenceSession detector;
    private readonly InferenceSession recognizer;
    private static readonly double[] Reference = [38.2946, 51.6963, 73.5318, 51.5014,
        56.0252, 71.7366, 41.5493, 92.3655, 70.7299, 92.2041];

    public FaceEngine(byte[] detectionModel, byte[] recognitionModel)
    {
        using var options = new SessionOptions { IntraOpNumThreads = 2, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        detector = new InferenceSession(detectionModel, options);
        try { recognizer = new InferenceSession(recognitionModel, options); }
        catch { detector.Dispose(); throw; }
    }

    public FaceExtractionResult Extract(int[] pixels, int width, int height, FaceExtractionOptions? options = null)
    {
        options ??= FaceExtractionOptions.Default;
        if (width < 112 || height < 112 || pixels.Length != width * height)
            return FaceExtractionResult.Retry("La imagen es demasiado pequeña. Repite la captura con mayor resolución.");
        const int side = 640;
        double scale = Math.Min((double)side / width, (double)side / height);
        var input = new float[3 * side * side];
        for (int y = 0; y < (int)(height * scale); y++)
        for (int x = 0; x < (int)(width * scale); x++)
        {
            int p = pixels[Math.Min(height - 1, (int)(y / scale)) * width + Math.Min(width - 1, (int)(x / scale))];
            int i = y * side + x;
            input[i] = p & 255; // YuNet: BGR, raw 0..255, NCHW.
            input[side * side + i] = (p >> 8) & 255;
            input[2 * side * side + i] = (p >> 16) & 255;
        }
        using var result = detector.Run([NamedOnnxValue.CreateFromTensor(detector.InputMetadata.Keys.Single(),
            new DenseTensor<float>(input, [1, 3, side, side]))]);
        var outputs = result.ToDictionary(v => v.Name, v => v.AsTensor<float>().ToArray());
        var candidates = new List<Face>();
        foreach (int stride in new[] { 8, 16, 32 })
        {
            var cls = outputs[$"cls_{stride}"]; var obj = outputs[$"obj_{stride}"];
            var box = outputs[$"bbox_{stride}"]; var kps = outputs[$"kps_{stride}"];
            int cols = side / stride;
            for (int i = 0; i < cls.Length; i++)
            {
                float score = MathF.Sqrt(Math.Clamp(cls[i], 0, 1) * Math.Clamp(obj[i], 0, 1));
                if (!float.IsFinite(score) || score < .8f) continue;
                double cx = (i % cols + box[4 * i]) * stride / scale;
                double cy = (i / cols + box[4 * i + 1]) * stride / scale;
                double w = Math.Exp(box[4 * i + 2]) * stride / scale;
                double h = Math.Exp(box[4 * i + 3]) * stride / scale;
                var points = new double[10];
                for (int j = 0; j < 5; j++)
                {
                    points[2 * j] = (kps[i * 10 + 2 * j] + i % cols) * stride / scale;
                    points[2 * j + 1] = (kps[i * 10 + 2 * j + 1] + i / cols) * stride / scale;
                }
                candidates.Add(new Face(cx - w / 2, cy - h / 2, w, h, score, points));
            }
        }
        var faces = new List<Face>();
        foreach (var candidate in candidates.OrderByDescending(f => f.Score))
            if (faces.All(f => IoU(f, candidate) < .3)) faces.Add(candidate);
        if (faces.Count != 1)
            return FaceExtractionResult.Retry(faces.Count == 0 ? "No se detectó un rostro. Mira de frente y mejora la iluminación." :
                "Hay más de un rostro. Fotografía únicamente a la persona que vas a registrar.");
        var face = faces[0];
        var framingFeedback = GetFramingFeedback(face.X, face.Y, face.W, face.H, width, height);
        if (framingFeedback is not null) return FaceExtractionResult.Retry(framingFeedback);
        var aligned = Align(pixels, width, height, face.Points, options);
        if (aligned.Pixels is null) return FaceExtractionResult.Retry(aligned.Error!);
        using var embedding = recognizer.Run([NamedOnnxValue.CreateFromTensor(recognizer.InputMetadata.Keys.Single(),
            new DenseTensor<float>(aligned.Pixels, [1, 3, 112, 112]))]);
        return FaceExtractionResult.Accepted(Normalize(embedding.First().AsTensor<float>().ToArray()));
    }

    internal static string? GetFramingFeedback(double x, double y, double w, double h, int width, int height)
    {
        if (x < 0 || y < 0 || x + w > width || y + h > height)
            return "El rostro queda cortado por el borde. Aléjate un poco y céntralo para que se vea completo; vuelve a capturar.";
        if (w < 100 || h < 100)
            return "El rostro se ve demasiado pequeño. Acércate a la cámara y vuelve a capturar.";
        return null;
    }

    // Least-squares similarity transform from five landmarks, inverted for bilinear sampling.
    private static (float[]? Pixels, string? Error) Align(int[] pixels, int width, int height, double[] points, FaceExtractionOptions options)
    {
        double sx = 0, sy = 0, dx = 0, dy = 0;
        for (int i = 0; i < 5; i++) { sx += points[i * 2] / 5; sy += points[i * 2 + 1] / 5; dx += Reference[i * 2] / 5; dy += Reference[i * 2 + 1] / 5; }
        double a = 0, b = 0, denominator = 0;
        for (int i = 0; i < 5; i++)
        {
            double x = points[2 * i] - sx, y = points[2 * i + 1] - sy;
            double u = Reference[2 * i] - dx, v = Reference[2 * i + 1] - dy;
            a += x * u + y * v; b += x * v - y * u; denominator += x * x + y * y;
        }
        if (denominator < 1e-6) return (null, "No se pudo alinear el rostro. Mira de frente y vuelve a capturar.");
        a /= denominator; b /= denominator;
        double det = a * a + b * b;
        if (det < 1e-8) return (null, "El rostro no tiene una posición válida. Mira de frente y vuelve a capturar.");
        double error = 0;
        for (int i = 0; i < 5; i++)
        {
            double ex = a * (points[2 * i] - sx) - b * (points[2 * i + 1] - sy) + dx - Reference[2 * i];
            double ey = b * (points[2 * i] - sx) + a * (points[2 * i + 1] - sy) + dy - Reference[2 * i + 1];
            error += ex * ex + ey * ey;
        }
        if (Math.Sqrt(error / 5) > options.MaxAlignmentError) return (null, options.AlignmentErrorMessage);
        var tensor = new float[3 * 112 * 112];
        for (int y = 0; y < 112; y++)
        for (int x = 0; x < 112; x++)
        {
            double px = (a * (x - dx) + b * (y - dy)) / det + sx;
            double py = (-b * (x - dx) + a * (y - dy)) / det + sy;
            int x0 = (int)Math.Floor(px), y0 = (int)Math.Floor(py);
            double fx = px - x0, fy = py - y0;
            for (int c = 0; c < 3; c++)
            {
                float Channel(int xx, int yy) => xx < 0 || yy < 0 || xx >= width || yy >= height ? 0 : (pixels[yy * width + xx] >> (16 - 8 * c)) & 255;
                tensor[c * 112 * 112 + y * 112 + x] = (float)(Channel(x0, y0) * (1 - fx) * (1 - fy) +
                    Channel(x0 + 1, y0) * fx * (1 - fy) + Channel(x0, y0 + 1) * (1 - fx) * fy + Channel(x0 + 1, y0 + 1) * fx * fy);
            }
        }
        return (tensor, null); // SFace: RGB, raw 0..255. Normalization is inside the model.
    }

    public static float[] Normalize(float[] vector)
    {
        if (vector.Length != VectorSize || vector.Any(v => !float.IsFinite(v))) throw new InvalidOperationException("Vector facial inválido.");
        double norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        if (norm < 1e-8) throw new InvalidOperationException("No se pudo generar el vector facial.");
        return vector.Select(v => (float)(v / norm)).ToArray();
    }

    private static double IoU(Face a, Face b)
    {
        double overlap = Math.Max(0, Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X)) *
            Math.Max(0, Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y));
        return overlap / (a.W * a.H + b.W * b.H - overlap);
    }

    public void Dispose() { detector.Dispose(); recognizer.Dispose(); }
    private sealed record Face(double X, double Y, double W, double H, float Score, double[] Points);
}
