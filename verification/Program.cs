using ExaTareo;
using System.Drawing;
using System.Security.Cryptography;

string root = Path.GetFullPath(args[0]);
using var engine = new FaceEngine(File.ReadAllBytes(Path.Combine(root, "ExaTareo/Resources/Raw/face_detection_yunet_2023mar.onnx")),
    File.ReadAllBytes(Path.Combine(root, "ExaTareo/Resources/Raw/face_recognition_sface_2021dec.onnx")));
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
void Rejected(Action action, string name)
{
    bool rejected = false;
    try { action(); } catch (InvalidOperationException) { rejected = true; } catch (CryptographicException) { rejected = true; }
    Check(rejected, name);
}
(int[] Pixels, int Width, int Height) Read(string path)
{
    using var bitmap = new Bitmap(path);
    var pixels = new int[bitmap.Width * bitmap.Height];
    for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) pixels[y * bitmap.Width + x] = bitmap.GetPixel(x, y).ToArgb();
    return (pixels, bitmap.Width, bitmap.Height);
}
var image = Read(Path.Combine(root, "verification/fixtures/face.jpg"));
float[] RequireVector(FaceExtractionResult result) => result.Vector ?? throw new Exception(result.Error);
var vector = RequireVector(engine.Extract(image.Pixels, image.Width, image.Height));
Check(vector.Length == 128 && Math.Abs(vector.Sum(v => (double)v * v) - 1) < .0001, "Real model: 128 normalized values");
var repeat = RequireVector(engine.Extract(image.Pixels, image.Width, image.Height));
Check(vector.Zip(repeat).All(p => Math.Abs(p.First - p.Second) < 1e-5), "Same capture: deterministic embedding");
var brighter = image.Pixels.Select(p => unchecked((int)0xff000000) |
    (Math.Min(255, ((p >> 16) & 255) + 10) << 16) | (Math.Min(255, ((p >> 8) & 255) + 10) << 8) | Math.Min(255, (p & 255) + 10)).ToArray();
var variation = RequireVector(engine.Extract(brighter, image.Width, image.Height));
double similarity = vector.Zip(variation).Sum(p => (double)p.First * p.Second);
Check(similarity > .8, $"Small lighting change: cosine {similarity:F4}");
var noFace = engine.Extract(new int[640 * 640], 640, 640);
Check(!noFace.IsSuccess && noFace.Vector is null && noFace.Error is not null, "No face returns retry feedback without throwing");
var lowResolution = engine.Extract(new int[64 * 64], 64, 64);
Check(!lowResolution.IsSuccess && lowResolution.Vector is null, "Low-resolution photo returns feedback without throwing");
Check(FaceEngine.GetFramingFeedback(200, 200, 80, 80, 640, 480)!.Contains("Acércate"), "Small face requests moving closer");
Check(FaceEngine.GetFramingFeedback(-1, 100, 200, 200, 640, 480)!.Contains("Aléjate"), "Clipped face requests moving back, not closer");
Check(FaceEngine.GetFramingFeedback(0, 0, 640, 480, 640, 480) is null, "Face on image boundary is accepted");
var pair = new int[image.Width * 2 * image.Height];
for (int y = 0; y < image.Height; y++)
{
    Array.Copy(image.Pixels, y * image.Width, pair, y * image.Width * 2, image.Width);
    Array.Copy(image.Pixels, y * image.Width, pair, y * image.Width * 2 + image.Width, image.Width);
}
var multipleFaces = engine.Extract(pair, image.Width * 2, image.Height);
Check(!multipleFaces.IsSuccess && multipleFaces.Vector is null, "Two faces return retry feedback without throwing");
Check(FaceExtractionOptions.GuidedEnrollment.MaxAlignmentError > FaceExtractionOptions.Default.MaxAlignmentError,
    "Guided enrollment accepts more head angle than identity check");
Check(FaceExtractionOptions.ForAngle(CaptureAngle.Front).MaxAlignmentError == FaceExtractionOptions.Default.MaxAlignmentError,
    "Front enrollment keeps strict frontal validation");
Check(FaceExtractionOptions.ForAngle(CaptureAngle.Left).MaxAlignmentError > FaceExtractionOptions.ForAngle(CaptureAngle.Up).MaxAlignmentError,
    "Side enrollment accepts more pose variation than vertical enrollment");
Check(FaceExtractionOptions.ForAngle(CaptureAngle.Left).AlignmentErrorMessage.Contains("Gira solo un poco"),
    "Side enrollment uses side-specific alignment feedback");
byte[] key = RandomNumberGenerator.GetBytes(32);
var blob = VectorCodec.Encrypt(vector, key, "001", "Prueba");
var plain = VectorCodec.Decrypt(blob, key, "001", "Prueba");
Check(blob.Length == 541 && vector.Zip(plain).All(p => Math.Abs(p.First - p.Second) < 1e-6), "Encrypted vector round trip");
var captures = new[]
{
    new FaceCaptureVector(CaptureAngle.Front, vector),
    new FaceCaptureVector(CaptureAngle.Left, variation),
    new FaceCaptureVector(CaptureAngle.Right, repeat)
};
var templateBlob = VectorCodec.EncryptTemplate(captures, key, "001", "Prueba");
var decodedTemplate = VectorCodec.DecryptTemplate(templateBlob, key, "001", "Prueba");
Check(templateBlob.Length > 541, "Multi-vector encrypted template needs variable-length storage");
Check(decodedTemplate.Count == 3, "Multi-vector encrypted template preserves capture count");
Check(decodedTemplate[0].Angle == CaptureAngle.Front, "Multi-vector encrypted template preserves angles");
Check(decodedTemplate[0].Vector.Length == 128, "Multi-vector encrypted template stores 128-value vectors");
Rejected(() => VectorCodec.DecryptTemplate(templateBlob, key, "002", "Prueba"), "Cannot swap multi-vector template to another ID");
Rejected(() => VectorCodec.EncryptTemplate(Array.Empty<FaceCaptureVector>(), key, "001", "Prueba"), "Empty multi-vector template rejected");
Check(!blob.SequenceEqual(VectorCodec.Encrypt(vector, key, "001", "Prueba")), "Unique random nonce per save");
Rejected(() => VectorCodec.Decrypt(blob, key, "002", "Prueba"), "Cannot swap encrypted vector to another ID");
Rejected(() => VectorCodec.Decrypt(blob, key, "001", "Otro"), "Cannot swap encrypted vector to another username");
Rejected(() => VectorCodec.Decrypt(blob, RandomNumberGenerator.GetBytes(32), "001", "Prueba"), "Wrong key rejected");
blob[^1] ^= 1;
Rejected(() => VectorCodec.Decrypt(blob, key, "001", "Prueba"), "Tampering rejected");
Rejected(() => VectorCodec.Encrypt(new float[128], key, "001", "Prueba"), "Zero vector rejected");
Rejected(() => VectorCodec.Encrypt(new float[127], key, "001", "Prueba"), "Wrong dimensions rejected");
var invalid = new float[128]; invalid[0] = float.NaN;
Rejected(() => VectorCodec.Encrypt(invalid, key, "001", "Prueba"), "Nonfinite vector rejected");
Console.WriteLine("Verification finished. No biometric vectors printed or persisted.");
Check(FaceMatcher.Compare(plain, variation) >= FaceMatcher.Threshold, "Stored encrypted vector matches real lighting variation");
Check(Math.Abs(FaceMatcher.Compare(vector, repeat) - 1) < .00001, "Identical face matches");
var orthogonalA = new float[128]; orthogonalA[0] = 1;
var orthogonalB = new float[128]; orthogonalB[1] = 1;
Check(FaceMatcher.Compare(orthogonalA, orthogonalB) < FaceMatcher.Threshold, "Unrelated synthetic vectors do not match (math only)");
Rejected(() => FaceMatcher.Compare(vector, new float[128]), "Invalid candidate cannot match");
Rejected(() => FaceMatcher.Compare(new float[127], vector), "Invalid stored dimensions cannot match");
var personA = FaceTemplate.Create("A", "Persona A", [new FaceCaptureVector(CaptureAngle.Front, vector)]);
var personB = FaceTemplate.Create("B", "Persona B", [new FaceCaptureVector(CaptureAngle.Front, orthogonalA)]);
var identified = FaceMatcher.Identify([personA, personB], repeat);
Check(identified.IsMatch && identified.Person!.Id == "A", "Identification selects best matching profile");
var noMatch = FaceMatcher.Identify([personB], repeat);
Check(!noMatch.IsMatch, "Identification rejects low score");
var ambiguousB = FaceTemplate.Create("C", "Persona C", [new FaceCaptureVector(CaptureAngle.Front, repeat)]);
var ambiguous = FaceMatcher.Identify([personA, ambiguousB], repeat);
Check(!ambiguous.IsMatch && ambiguous.IsAmbiguous, "Identification rejects ambiguous top-two scores");
