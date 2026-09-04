using CommunityToolkit.Maui.Core.Primitives;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;
using Path = System.IO.Path;

namespace ExaTareo;

internal sealed class FaceCameraPage : ContentPage
{
    private readonly CameraView camera = new();
    private readonly FaceScannerOverlay scanner = new();
    private readonly Label hint = new() { Text = "Iniciando cámara…", TextColor = Color.FromArgb("#A8C7D0"), FontSize = 13, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Button capture = new() { Text = "CAPTURAR", IsEnabled = false, BackgroundColor = Color.FromArgb("#2FBF71"), TextColor = Colors.White, CornerRadius = 18 };
    private readonly Button cancel = new() { Text = "Cancelar", BackgroundColor = Color.FromArgb("#173541"), TextColor = Colors.White, CornerRadius = 18 };
    private readonly TaskCompletionSource<FileResult?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim cameraGate = new(1, 1);
    private readonly string instruction;
    private bool initialized, closing, stopped;

    private FaceCameraPage(string? instruction)
    {
        this.instruction = instruction ?? "Mira de frente a la cámara.";
        BackgroundColor = Color.FromArgb("#08151C");
        Padding = 18;
        var viewport = new Grid { MinimumHeightRequest = 250 };
        viewport.Add(camera);
        viewport.Add(scanner);
        var frame = new Border { Stroke = Color.FromArgb("#29414B"), StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 24 }, BackgroundColor = Color.FromArgb("#0D2029"), Content = viewport };
        var buttons = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        buttons.Add(cancel); buttons.Add(capture, 1, 0);
        var layout = new Grid { RowSpacing = 16, MaximumWidthRequest = 560,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } };
        layout.Add(new Label { Text = "Registro facial", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center });
        layout.Add(frame, 0, 1);
        layout.Add(new Label { Text = "Ubica tu rostro dentro del óvalo", FontSize = 18, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center }, 0, 2);
        layout.Add(hint, 0, 3);
        layout.Add(buttons, 0, 4);
        Content = layout;
        camera.Loaded += async (_, _) => await InitializeAsync();
        capture.Clicked += async (_, _) => await TakePhotoAsync();
        cancel.Clicked += async (_, _) => await CloseAsync(null);
    }

    public static async Task<FileResult?> CaptureAsync(Page owner, string? instruction = null)
    {
        var page = new FaceCameraPage(instruction);
        await owner.Navigation.PushModalAsync(page);
        return await page.completion.Task;
    }

    private async Task InitializeAsync()
    {
        if (initialized || closing) return;
        initialized = true;
        await cameraGate.WaitAsync();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var cameras = await camera.GetAvailableCameras(timeout.Token);
            camera.SelectedCamera = cameras.FirstOrDefault(c => c.Position == CameraPosition.Front) ?? cameras.FirstOrDefault();
            if (camera.SelectedCamera is null) { hint.Text = "No hay una cámara disponible en este dispositivo."; return; }
            await camera.StartCameraPreview(timeout.Token);
            if (closing) return;
            hint.Text = $"{instruction} Pulsa Capturar; la imagen se validará después de tomarla.";
            capture.IsEnabled = true;
            scanner.Start();
        }
        catch (OperationCanceledException) { if (!closing) hint.Text = "La cámara tardó demasiado. Cierra esta pantalla e intenta nuevamente."; }
        catch { if (!closing) hint.Text = "No se pudo abrir la cámara. Revisa los permisos y vuelve a intentarlo."; }
        finally { cameraGate.Release(); }
    }

    private async Task TakePhotoAsync()
    {
        if (!capture.IsEnabled || closing) return;
        capture.IsEnabled = false;
        hint.Text = "Tomando la fotografía…";
        await cameraGate.WaitAsync();
        FileResult? photo = null;
        string? path = null;
        var received = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCaptured(object? sender, MediaCapturedEventArgs args)
        { if (!received.TrySetResult(args.Media)) args.Media.Dispose(); }
        void OnFailed(object? sender, MediaCaptureFailedEventArgs args)
        { received.TrySetException(new IOException("No se pudo capturar la fotografía.")); }
        camera.MediaCaptured += OnCaptured;
        camera.MediaCaptureFailed += OnFailed;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await camera.CaptureImage(timeout.Token);
            using var image = await received.Task.WaitAsync(timeout.Token);
            if (image.CanSeek) image.Position = 0;
            path = Path.Combine(FileSystem.CacheDirectory, $"camera-face-{Guid.NewGuid():N}.jpg");
            await using (var output = File.Create(path)) await image.CopyToAsync(output, timeout.Token);
            photo = new FileResult(path);
        }
        catch { if (!closing) hint.Text = "No se pudo tomar la foto. Puedes volver a capturar."; }
        finally
        {
            camera.MediaCaptured -= OnCaptured;
            camera.MediaCaptureFailed -= OnFailed;
            if (photo is null && path is not null && File.Exists(path)) File.Delete(path);
            capture.IsEnabled = !closing;
            cameraGate.Release();
        }
        if (photo is not null)
        {
            if (closing) { File.Delete(photo.FullPath); return; }
            await CloseAsync(photo);
        }
    }

    private void StopResources()
    {
        if (stopped) return;
        stopped = true;
        scanner.Stop();
        try { camera.StopCameraPreview(); }
        finally { camera.Handler?.DisconnectHandler(); }
    }

    private async Task CloseAsync(FileResult? result)
    {
        if (closing) return;
        closing = true;
        capture.IsEnabled = false; cancel.IsEnabled = false;
        lifetime.Cancel();
        await cameraGate.WaitAsync();
        try { StopResources(); }
        catch { /* Closing must still release the modal result if a camera driver fails. */ }
        finally { cameraGate.Release(); }
        try { await Navigation.PopModalAsync(); }
        finally { completion.TrySetResult(result); }
    }

    protected override bool OnBackButtonPressed() { _ = CloseAsync(null); return true; }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        scanner.Stop();
        if (!closing) _ = CloseAsync(null);
    }
}
