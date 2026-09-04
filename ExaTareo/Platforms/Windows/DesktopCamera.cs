using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Storage;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Maui.Platform;

namespace ExaTareo;

internal static class DesktopCamera
{
    public static async Task<FileResult?> CaptureAsync(Window owner, string? instruction = null)
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        if (devices.Count == 0) throw new InvalidOperationException("No se encontró una webcam. Conecta una cámara e intenta nuevamente.");
        var device = devices.FirstOrDefault(d => d.EnclosureLocation?.Panel == Windows.Devices.Enumeration.Panel.Front) ?? devices[0];
        using var camera = new MediaCapture();
        try
        {
            await camera.InitializeAsync(new MediaCaptureInitializationSettings {
                VideoDeviceId = device.Id, StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl, MemoryPreference = MediaCaptureMemoryPreference.Cpu });
        }
        catch (UnauthorizedAccessException)
        { throw new InvalidOperationException("Permite el acceso a la cámara para aplicaciones de escritorio en Configuración > Privacidad y seguridad > Cámara."); }
        var source = camera.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color && s.Info.MediaStreamType == MediaStreamType.VideoPreview)
            ?? camera.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
        if (source is null) throw new InvalidOperationException("Esta webcam no ofrece una vista previa compatible.");
        using var player = new MediaPlayer { RealTimePlayback = true, AutoPlay = false };
        using var mediaSource = MediaSource.CreateFromMediaFrameSource(source);
        player.Source = mediaSource;
        var preview = new MediaPlayerElement { Width = 480, Height = 430, AreTransportControlsEnabled = false };
        preview.SetMediaPlayer(player);
        var scanner = new FaceScannerOverlay();
        var viewport = new Microsoft.UI.Xaml.Controls.Grid { Width = 480, Height = 430 };
        viewport.Children.Add(preview);
        viewport.Children.Add(scanner.ToPlatform(owner.Handler!.MauiContext!));
        var contents = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 14 };
        contents.Children.Add(new Microsoft.UI.Xaml.Controls.Border {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 13, 32, 41)),
            CornerRadius = new Microsoft.UI.Xaml.CornerRadius(24), Child = viewport });
        contents.Children.Add(new TextBlock { Text = "Ubica tu rostro dentro del óvalo", FontSize = 18,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center });
        contents.Children.Add(new TextBlock { Text = $"{instruction ?? "Mira de frente a la cámara."} Pulsa Capturar; la imagen se validará después de tomarla.",
            FontSize = 12, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 168, 199, 208)),
            TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center });
        var nativeWindow = (Microsoft.UI.Xaml.Window)owner.Handler!.PlatformView!;
        var dialog = new ContentDialog {
            Title = "Registro facial", Content = contents,
            RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 13, 32, 41)),
            PrimaryButtonText = "Capturar", CloseButtonText = "Cancelar",
            XamlRoot = nativeWindow.Content.XamlRoot };
        string? capturePath = null;
        bool success = false;
        try
        {
            player.Play();
            scanner.Start();
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
            var folder = await StorageFolder.GetFolderFromPathAsync(FileSystem.CacheDirectory);
            var photo = await folder.CreateFileAsync($"desktop-face-{Guid.NewGuid():N}.jpg", CreationCollisionOption.FailIfExists);
            capturePath = photo.Path;
            await camera.CapturePhotoToStorageFileAsync(ImageEncodingProperties.CreateJpeg(), photo);
            success = true;
            return new FileResult(photo.Path);
        }
        finally
        {
            scanner.Stop();
            scanner.Handler?.DisconnectHandler();
            player.Pause(); preview.SetMediaPlayer(null); player.Source = null;
            if (!success && capturePath is not null && File.Exists(capturePath)) File.Delete(capturePath);
        }
    }
}
