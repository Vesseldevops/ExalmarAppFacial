using Microsoft.Maui.Controls.Shapes;
using Path = System.IO.Path;

namespace ExaTareo;

public sealed class EnrollmentCapturePage : ContentPage
{
    private static readonly Color Sea = Color.FromArgb("#0B5966");
    private static readonly Color Ink = Color.FromArgb("#10212B");
    private static readonly Color Muted = Color.FromArgb("#667789");
    private static readonly Color Success = Color.FromArgb("#0E9F5B");
    private readonly EnrollmentService enrollment = new();
    private readonly TaskCompletionSource<IReadOnlyList<FaceCaptureVector>?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<FaceCaptureVector> captures = [];
    private readonly Label title = Text("", 25, Ink, true);
    private readonly Label progress = Text("", 13, Sea, true);
    private readonly Label guidance = Text("", 15, Muted);
    private readonly Label status = Text("", 13, Muted);
    private readonly VerticalStackLayout completed = new() { Spacing = 8 };
    private readonly Button saveStep = new() { Text = "GUARDAR CAPTURA", BackgroundColor = Sea, TextColor = Colors.White, CornerRadius = 18, HeightRequest = 54 };
    private readonly Button cancel = new() { Text = "Cancelar", BackgroundColor = Color.FromArgb("#E1EDEF"), TextColor = Sea, CornerRadius = 18, HeightRequest = 48 };
    private readonly ActivityIndicator busy = new() { Color = Sea, IsVisible = false };
    private bool working, closing;

    private EnrollmentCapturePage(string name)
    {
        Title = "Captura guiada";
        BackgroundColor = Color.FromArgb("#EEF4F5");
        var body = new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 18,
            MaximumWidthRequest = 620,
            Children =
            {
                Text("EXALMAR · ENROLLMENT", 22, Sea, true),
                Card(new VerticalStackLayout
                {
                    Spacing = 18,
                    Children =
                    {
                        Text($"Perfil de {name.Trim()}", 14, Sea, true),
                        progress,
                        title,
                        guidance,
                        Text("Capturaremos cinco ángulos. Mantén una sola persona en cámara y buena iluminación.", 13, Muted),
                        saveStep,
                        busy,
                        status,
                        completed
                    }
                }),
                cancel
            }
        };
        Content = new ScrollView { Content = body };
        saveStep.Clicked += CaptureStepAsync;
        cancel.Clicked += async (_, _) => await CloseAsync(null);
        RefreshStep();
    }

    public static async Task<IReadOnlyList<FaceCaptureVector>?> CaptureAsync(Page owner, string name)
    {
        var page = new EnrollmentCapturePage(name);
        await owner.Navigation.PushModalAsync(page);
        return await page.completion.Task;
    }

    private async void CaptureStepAsync(object? sender, EventArgs e)
    {
        if (working || captures.Count >= CaptureAngles.Required.Count) return;
        var angle = CaptureAngles.Required[captures.Count];
        SetWorking(true);
        FileResult? photo = null;
        try
        {
            status.TextColor = Muted;
            status.Text = "Abriendo cámara…";
#if WINDOWS
            photo = await DesktopCamera.CaptureAsync(Window, angle.Guidance());
#else
            if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
            {
                status.Text = "Permite el acceso a la cámara para registrar el rostro.";
                return;
            }
            photo = await FaceCameraPage.CaptureAsync(this, angle.Guidance());
#endif
            if (photo is null)
            {
                status.Text = "Captura cancelada. Este ángulo queda pendiente.";
                return;
            }
            status.Text = "Validando rostro y generando vector…";
            var result = await enrollment.ExtractAsync(photo, FaceExtractionOptions.ForAngle(angle));
            if (!result.IsSuccess)
            {
                status.TextColor = Color.FromArgb("#B42318");
                status.Text = result.Error;
                return;
            }
            captures.Add(new FaceCaptureVector(angle, result.Vector!));
            status.TextColor = Success;
            status.Text = $"{angle.Label()} guardado.";
            if (captures.Count == CaptureAngles.Required.Count)
            {
                await CloseAsync(captures.ToList());
                return;
            }
            RefreshStep();
        }
        catch (InvalidOperationException ex) { status.TextColor = Color.FromArgb("#B42318"); status.Text = ex.Message; }
        catch (PermissionException) { status.TextColor = Color.FromArgb("#B42318"); status.Text = "No se concedió permiso para usar la cámara."; }
        catch { status.TextColor = Color.FromArgb("#B42318"); status.Text = "No se pudo procesar la captura. Intenta nuevamente."; }
        finally
        {
            DeleteTemporaryPhoto(photo);
            SetWorking(false);
        }
    }

    private void RefreshStep()
    {
        var angle = CaptureAngles.Required[captures.Count];
        progress.Text = $"Paso {captures.Count + 1} de {CaptureAngles.Required.Count}";
        title.Text = angle.Label();
        guidance.Text = angle.Guidance();
        completed.Children.Clear();
        if (captures.Count == 0)
        {
            completed.Children.Add(Text("Aún no hay capturas guardadas.", 13, Muted));
            return;
        }
        completed.Children.Add(Text("Capturas listas", 14, Ink, true));
        foreach (var capture in captures)
            completed.Children.Add(Text($"✓ {capture.Angle.Label()}", 13, Success));
    }

    private void SetWorking(bool value)
    {
        working = value;
        busy.IsVisible = value;
        busy.IsRunning = value;
        saveStep.IsEnabled = !value;
        cancel.IsEnabled = !value;
    }

    private async Task CloseAsync(IReadOnlyList<FaceCaptureVector>? result)
    {
        if (closing) return;
        closing = true;
        saveStep.IsEnabled = false;
        cancel.IsEnabled = false;
        try { await Navigation.PopModalAsync(); }
        finally { completion.TrySetResult(result); }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    private static void DeleteTemporaryPhoto(FileResult? photo)
    {
        if (photo is null || string.IsNullOrEmpty(photo.FullPath)) return;
        try
        {
            var cache = Path.GetFullPath(FileSystem.CacheDirectory) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(photo.FullPath).StartsWith(cache, StringComparison.OrdinalIgnoreCase)) File.Delete(photo.FullPath);
        }
        catch { }
    }

    private static Label Text(string text, double size, Color color, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        TextColor = color,
        FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None
    };

    private static Border Card(View content) => new()
    {
        Content = content,
        Padding = 20,
        BackgroundColor = Colors.White,
        Stroke = Color.FromArgb("#DCE5EA"),
        StrokeShape = new RoundRectangle { CornerRadius = 24 }
    };
}
