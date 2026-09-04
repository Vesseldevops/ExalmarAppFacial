using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace ExaTareo;

public sealed class EnrollmentService
{
    private FaceEngine? engine;
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<FaceExtractionResult> ExtractAsync(FileResult photo, FaceExtractionOptions? options = null)
    {
        await gate.WaitAsync();
        try
        {
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
            // MAUI 8 FileResult(path) has no WinRT StorageFile; our webcam returns a local path.
            await using var stream = File.OpenRead(photo.FullPath);
            using var randomAccess = stream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(randomAccess);
            double scale = Math.Min(1, 1600d / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var transform = new BitmapTransform { ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * scale)), ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * scale)) };
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
            var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight];
            try
            {
                bitmap.CopyToBuffer(bytes.AsBuffer());
                Buffer.BlockCopy(bytes, 0, pixels, 0, bytes.Length);
                return await Task.Run(() => engine.Extract(pixels, bitmap.PixelWidth, bitmap.PixelHeight, options));
            }
            finally { Array.Clear(bytes); Array.Clear(pixels); }
        }
        finally { gate.Release(); }
    }
}
