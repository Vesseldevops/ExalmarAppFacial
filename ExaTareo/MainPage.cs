using Microsoft.Maui.Controls.Shapes;
using Path = System.IO.Path;

namespace ExaTareo;

public sealed class MainPage : ContentPage
{
    private static readonly Color Sea = Color.FromArgb("#0B5966");
    private static readonly Color Ink = Color.FromArgb("#10212B");
    private static readonly Color Muted = Color.FromArgb("#667789");
    private readonly Entry username = new() { Placeholder = "Nombre de usuario", MaxLength = 100 };
    private readonly CheckBox consent = new() { Color = Sea };
    private readonly Button capture = new() { Text = "CAPTURAR ROSTRO", BackgroundColor = Sea, TextColor = Colors.White, CornerRadius = 18, HeightRequest = 56 };
    private readonly Button save = new() { Text = "GUARDAR EN ESTE DISPOSITIVO", BackgroundColor = Color.FromArgb("#0E9F5B"), TextColor = Colors.White, CornerRadius = 18, HeightRequest = 56, IsEnabled = false, Opacity = .45 };
    private readonly Label status = Text("Captura pendiente · Sin datos guardados", 13, Muted);
    private readonly Label counter = Text("Personas registradas", 19, Ink, true);
    private readonly VerticalStackLayout people = new() { Spacing = 10 };
    private readonly ActivityIndicator busy = new() { Color = Sea, IsVisible = false };
    private readonly FaceRepository repository = new();
    private readonly EnrollmentService enrollment = new();
    private IReadOnlyList<FaceCaptureVector>? pendingCaptures;
    private bool working;
    private readonly Button verify = new() { Text = "VERIFICAR MI IDENTIDAD", BackgroundColor = Sea, TextColor = Colors.White, CornerRadius = 18 };

    public MainPage()
    {
        BackgroundColor = Color.FromArgb("#EEF4F5");
        Title = "Registro facial";
        username.TextColor = Ink; username.PlaceholderColor = Muted; username.BackgroundColor = Colors.Transparent;
        var header = new Border
        {
            Background = new LinearGradientBrush(new GradientStopCollection {
                new GradientStop(Sea, 0), new GradientStop(Color.FromArgb("#12384A"), 1) }, new Point(0, 0), new Point(1, 1)),
            StrokeThickness = 0, Padding = new Thickness(24, 28),
            Content = new VerticalStackLayout { Spacing = 5, Children = {
                Text("EXALMAR REGISTROS", 22, Colors.White, true),
                Text("Registro facial · Almacenamiento local", 13, Color.FromArgb("#CDE4E8")) } }
        };
        var permission = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        permission.Add(consent);
        permission.Add(new Label { Text = "La persona autoriza registrar su rostro en este dispositivo para la demo.", FontSize = 12, TextColor = Muted, VerticalOptions = LayoutOptions.Center }, 1, 0);
        var form = new VerticalStackLayout { Spacing = 16, Children = {
            Text("Registrar persona", 25, Ink, true),
            Text("Solo ID, nombre y vector facial. Procesamiento sin internet.", 13, Muted),
            Text("El ID se genera automáticamente al guardar.", 12, Sea),
            Text("NOMBRE DE USUARIO", 11, Muted, true), Input(username),
            Card(new VerticalStackLayout { Spacing = 8, Children = {
                Text("Cinco ángulos · Una persona", 16, Sea, true),
                Text("Frente, izquierda, derecha, arriba y abajo. Usa buena iluminación, sin mascarilla ni lentes oscuros.", 13, Muted) } }, "#F0F8F6"),
            permission, capture, busy, status, save,
            Text("Se guardan vectores reales cifrados. Esta etapa no pasa lista ni valida presencia ante fotos o videos.", 12, Muted)
        } };
        var body = new VerticalStackLayout { Padding = 18, Spacing = 20, MaximumWidthRequest = 620, HorizontalOptions = LayoutOptions.Fill,
            Children = { Card(new VerticalStackLayout { Spacing = 10, Children = { Text("¿Ya tienes un perfil?", 19, Ink, true), Text("Compara una nueva captura con tu rostro registrado.", 13, Muted), verify } }), Card(form), Card(new VerticalStackLayout { Spacing = 14, Children = { counter, people } }) } };
        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        layout.Add(header); layout.Add(new ScrollView { Content = body }, 0, 1);
        Content = layout;
        capture.Clicked += CaptureAsync;
        save.Clicked += SaveAsync;
        verify.Clicked += async (_, _) => { if (!working) await Navigation.PushModalAsync(new VerificationPage()); };
        username.TextChanged += (_, _) => InvalidateCapture();
        consent.CheckedChanged += (_, _) => { if (!consent.IsChecked) InvalidateCapture(); };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await RefreshAsync(); }
        catch { status.Text = "No se pudo abrir la base local. Cierra y vuelve a abrir la app."; }
    }

    private async void CaptureAsync(object? sender, EventArgs e)
    {
        if (working) return;
        InvalidateCapture();
        if (string.IsNullOrWhiteSpace(username.Text))
        { await DisplayAlert("Datos pendientes", "Escribe el nombre de usuario antes de capturar.", "Entendido"); return; }
        if (!consent.IsChecked) { await DisplayAlert("Autorización", "Confirma la autorización de la persona antes de capturar su rostro.", "Entendido"); return; }
        SetWorking(true);
        try
        {
            var captures = await EnrollmentCapturePage.CaptureAsync(this, username.Text.Trim());
            if (captures is null) { status.Text = "Captura cancelada. No se guardaron datos."; return; }
            pendingCaptures = captures;
            status.Text = $"Enrollment listo · {captures.Count} vectores reales listos para guardar.";
        }
        catch (InvalidOperationException ex) { status.Text = ex.Message; }
        catch (PermissionException) { status.Text = "No se concedió permiso para usar la cámara."; }
        catch { status.Text = "No se pudo completar el enrollment. Intenta nuevamente. No se guardaron datos."; }
        finally { SetWorking(false); }
    }

    private async void SaveAsync(object? sender, EventArgs e)
    {
        if (working || pendingCaptures is null) return;
        SetWorking(true);
        try
        {
            var generatedId = await repository.RegisterAsync(username.Text, pendingCaptures);
            InvalidateCapture(); username.Text = ""; consent.IsChecked = false;
            status.Text = $"Registro guardado en SQLite. ID: {generatedId}";
            try { await RefreshAsync(); }
            catch { status.Text += " Vuelve a abrir la app para actualizar la lista."; }
        }
        catch (InvalidOperationException ex) { status.Text = ex.Message; }
        catch { status.Text = "No se pudo guardar. No se reemplazó ningún registro; intenta nuevamente."; }
        finally { SetWorking(false); }
    }

    private async Task RefreshAsync()
    {
        var registered = await repository.ListAsync();
        counter.Text = $"Personas registradas · {registered.Count}";
        people.Children.Clear();
        if (registered.Count == 0) people.Children.Add(Text("Todavía no hay rostros registrados en este dispositivo.", 13, Muted));
        foreach (var person in registered)
            people.Children.Add(PersonCard(person));
    }

    private Border PersonCard(RegisteredPerson person)
    {
        var delete = new Button
        {
            Text = "Eliminar",
            BackgroundColor = Color.FromArgb("#FEE4E2"),
            TextColor = Color.FromArgb("#B42318"),
            CornerRadius = 14,
            HeightRequest = 42
        };
        delete.Clicked += async (_, _) => await DeletePersonAsync(person);
        return Card(new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                Text(person.NombreUsuario, 15, Ink, true),
                Text($"ID: {person.Id} · Plantilla facial guardada", 12, Sea),
                delete
            }
        }, "#F7FAFB");
    }

    private async Task DeletePersonAsync(RegisteredPerson person)
    {
        if (working) return;
        bool confirmed = await DisplayAlert("Eliminar perfil",
            $"Se eliminará el vector facial guardado de {person.NombreUsuario}.\nID: {person.Id}\n\nEsta acción no se puede deshacer.",
            "Eliminar", "Cancelar");
        if (!confirmed) return;
        SetWorking(true);
        try
        {
            await repository.DeleteAsync(person.Id);
            status.Text = $"Perfil eliminado: {person.NombreUsuario}";
            await RefreshAsync();
        }
        catch { status.Text = "No se pudo eliminar el perfil. Intenta nuevamente."; }
        finally { SetWorking(false); }
    }

    private void InvalidateCapture()
    {
        if (pendingCaptures is not null)
            foreach (var capture in pendingCaptures) Array.Clear(capture.Vector);
        pendingCaptures = null; save.IsEnabled = false;
        save.Opacity = .45;
        status.Text = "Captura pendiente · Sin datos nuevos guardados";
    }

    private void SetWorking(bool value)
    {
        working = value; busy.IsRunning = value; busy.IsVisible = value;
        verify.IsEnabled = !value;
        capture.IsEnabled = !value; username.IsEnabled = !value; consent.IsEnabled = !value;
        save.IsEnabled = !value && pendingCaptures is not null && consent.IsChecked;
        save.Opacity = save.IsEnabled ? 1 : .45;
    }

    private static Label Text(string text, double size, Color color, bool bold = false) => new() {
        Text = text, FontSize = size, TextColor = color, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None };
    private static Border Input(Entry input) => new() { Content = input, Padding = new Thickness(12, 2), Stroke = Color.FromArgb("#DCE5EA"), BackgroundColor = Color.FromArgb("#F9FBFC"), StrokeShape = new RoundRectangle { CornerRadius = 14 } };
    private static Border Card(View content, string color = "#FFFFFF") => new() {
        Content = content, Padding = 20, BackgroundColor = Color.FromArgb(color), Stroke = Color.FromArgb("#DCE5EA"), StrokeShape = new RoundRectangle { CornerRadius = 24 } };
}
