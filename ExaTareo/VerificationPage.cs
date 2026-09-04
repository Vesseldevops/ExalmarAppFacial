using Microsoft.Maui.Controls.Shapes;

namespace ExaTareo;

public sealed class VerificationPage : ContentPage
{
    private readonly FaceRepository repository = new();
    private readonly EnrollmentService enrollment = new();
    private readonly Label status = new() { Text = "Cargando perfiles…", FontSize = 16, TextColor = Color.FromArgb("#0B5966") };
    private readonly Button identify = new() { Text = "IDENTIFICAR", IsEnabled = false, BackgroundColor = Color.FromArgb("#0B5966"), TextColor = Colors.White, CornerRadius = 18 };
    private readonly Button next = new() { Text = "Siguiente", IsVisible = false, BackgroundColor = Color.FromArgb("#0E9F5B"), TextColor = Colors.White, CornerRadius = 18 };
    private readonly Button back = new() { Text = "Volver al registro", BackgroundColor = Color.FromArgb("#E1EDEF"), TextColor = Color.FromArgb("#0B5966"), CornerRadius = 18 };
    private readonly ActivityIndicator busy = new() { Color = Color.FromArgb("#0B5966") };
    private List<FaceTemplate> templates = [];
    private bool loaded, working;

    public VerificationPage()
    {
        Title = "Identificar persona";
        BackgroundColor = Color.FromArgb("#EEF4F5");
        Content = new ScrollView { Content = new VerticalStackLayout { Padding = 24, Spacing = 22, MaximumWidthRequest = 620, Children = {
            new Label { Text = "EXALMAR · IDENTIFICACIÓN", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0B5966") },
            new Border { Padding = 24, BackgroundColor = Colors.White, Stroke = Color.FromArgb("#DCE5EA"), StrokeShape = new RoundRectangle { CornerRadius = 24 }, Content = new VerticalStackLayout { Spacing = 20, Children = {
                new Label { Text = "Identificar persona", FontSize = 25, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#10212B") },
                new Label { Text = "Toma una nueva captura. La app buscará automáticamente el rostro más parecido entre los perfiles guardados en este dispositivo.", TextColor = Color.FromArgb("#667789") },
                identify, next, busy, status,
                new Label { Text = "Demo biométrica: puede equivocarse y no detecta suplantaciones con fotos o videos. La captura de identificación no se guarda en SQLite.", FontSize = 12, TextColor = Color.FromArgb("#667789") }
            } } }, back
        } } };
        identify.Clicked += IdentifyAsync;
        next.Clicked += async (_, _) => { if (!working) await Navigation.PopModalAsync(); };
        back.Clicked += async (_, _) => { if (!working) await Navigation.PopModalAsync(); };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (loaded) return;
        loaded = true;
        try
        {
            templates = await repository.ListTemplatesAsync();
            identify.IsEnabled = templates.Count > 0;
            status.Text = templates.Count == 0
                ? "No hay perfiles. Vuelve al registro y guarda un rostro primero."
                : $"Listo para identificar entre {templates.Count} perfiles registrados.";
        }
        catch { status.Text = "No se pudieron cargar los perfiles. Vuelve al registro e intenta nuevamente."; }
    }

    private async void IdentifyAsync(object? sender, EventArgs e)
    {
        if (working || templates.Count == 0) return;
        working = true;
        identify.IsEnabled = back.IsEnabled = next.IsEnabled = false;
        next.IsVisible = false;
        busy.IsRunning = true;
        status.TextColor = Color.FromArgb("#0B5966");
        status.Text = "Identificación pendiente…";
        FileResult? photo = null;
        float[]? candidate = null;
        try
        {
#if WINDOWS
            photo = await DesktopCamera.CaptureAsync(Window);
#else
            if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
            { status.Text = "Permite el acceso a la cámara para identificar el rostro."; return; }
            photo = await FaceCameraPage.CaptureAsync(this);
#endif
            if (photo is null) { status.Text = "Identificación cancelada. No se realizó una comparación."; return; }
            status.Text = "Procesando rostro y buscando perfil…";
            var extraction = await enrollment.ExtractAsync(photo);
            candidate = extraction.Vector;
            if (!extraction.IsSuccess) { status.TextColor = Color.FromArgb("#B42318"); status.Text = extraction.Error; return; }
            var result = FaceMatcher.Identify(templates, candidate!);
            if (result.IsMatch)
            {
                status.TextColor = Color.FromArgb("#0E9F5B");
                status.Text = $"Identificado: {result.Person!.NombreUsuario}\nSimilitud coseno: {result.Score:F3}";
                next.IsVisible = true;
                next.IsEnabled = true;
                return;
            }
            status.TextColor = Color.FromArgb("#B42318");
            status.Text = result.IsAmbiguous
                ? $"Resultado ambiguo. Vuelve a intentar con mejor iluminación.\nMayor similitud: {result.Score:F3} · Segundo lugar: {result.RunnerUpScore:F3}"
                : $"No se encontró un perfil confiable.\nMayor similitud: {result.Score:F3} · Umbral: {FaceMatcher.Threshold:F3}";
        }
        catch (InvalidOperationException ex) { status.TextColor = Color.FromArgb("#B42318"); status.Text = ex.Message; }
        catch { status.TextColor = Color.FromArgb("#B42318"); status.Text = "No se pudo identificar el rostro. Intenta nuevamente."; }
        finally
        {
            if (candidate is not null) Array.Clear(candidate);
            DeleteTemporaryPhoto(photo);
            working = false;
            busy.IsRunning = false;
            back.IsEnabled = true;
            identify.IsEnabled = templates.Count > 0;
        }
    }

    protected override bool OnBackButtonPressed() => working || base.OnBackButtonPressed();

    private static void DeleteTemporaryPhoto(FileResult? photo)
    {
        if (photo is null || string.IsNullOrEmpty(photo.FullPath)) return;
        try
        {
            var cache = System.IO.Path.GetFullPath(FileSystem.CacheDirectory) + System.IO.Path.DirectorySeparatorChar;
            if (System.IO.Path.GetFullPath(photo.FullPath).StartsWith(cache, StringComparison.OrdinalIgnoreCase)) File.Delete(photo.FullPath);
        }
        catch { }
    }
}
