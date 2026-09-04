using Android.Graphics;
using Android.Media;
using Path = System.IO.Path;

namespace ExaTareo;

public sealed class EnrollmentService
{
    private FaceEngine? engine;
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<FaceExtractionResult> ExtractAsync(FileResult photo, FaceExtractionOptions? options = null)
    {
        await gate.WaitAsync();
        string temp = Path.Combine(FileSystem.CacheDirectory, $"enroll-{Guid.NewGuid():N}.jpg");
        try
        {
            await using (var source = await photo.OpenReadAsync())
            await using (var destination = File.Create(temp)) await source.CopyToAsync(destination);
            if (engine is null)
            {
                static async Task<byte[]> Asset(string name)
                {
                    await using var stream = await FileSystem.OpenAppPackageFileAsync(name);
                    using var data = new MemoryStream(); await stream.CopyToAsync(data); return data.ToArray();
                }
                var detection = await Asset("face_detection_yunet_2023mar.onnx");
                var recognition = await Asset("face_recognition_sface_2021dec.onnx");
                engine = await Task.Run(() => new FaceEngine(detection, recognition));
            }
            return await Task.Run(() =>
            {
                using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
                BitmapFactory.DecodeFile(temp, bounds);
                int sample = 1;
                while (Math.Max(bounds.OutWidth, bounds.OutHeight) / sample > 1600) sample *= 2;
                using var decodeOptions = new BitmapFactory.Options { InSampleSize = sample };
                using var bitmap = BitmapFactory.DecodeFile(temp, decodeOptions) ?? throw new InvalidOperationException("No se pudo leer la fotografía.");
                using var exif = new ExifInterface(temp);
                int orientation = exif.GetAttributeInt(ExifInterface.TagOrientation, 1);
                using var matrix = new Matrix();
                switch (orientation)
                {
                    case 2: matrix.SetScale(-1, 1); break;
                    case 3: matrix.SetRotate(180); break;
                    case 4: matrix.SetScale(1, -1); break;
                    case 5: matrix.SetRotate(90); matrix.PostScale(-1, 1); break;
                    case 6: matrix.SetRotate(90); break;
                    case 7: matrix.SetRotate(-90); matrix.PostScale(-1, 1); break;
                    case 8: matrix.SetRotate(-90); break;
                }
                using var upright = Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true)!;
                var pixels = new int[upright.Width * upright.Height];
                try
                {
                    upright.GetPixels(pixels, 0, upright.Width, 0, 0, upright.Width, upright.Height);
                    return engine.Extract(pixels, upright.Width, upright.Height, options);
                }
                finally { Array.Clear(pixels); }
            });
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            finally { gate.Release(); }
        }
    }
}
