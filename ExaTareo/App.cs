namespace ExaTareo;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);
#if WINDOWS
        window.Title = "Exalmar · Registro facial";
        window.Width = 980;
        window.Height = 900;
        window.MinimumWidth = 640;
        window.MinimumHeight = 600;
#endif
        return window;
    }

    public App()
    {
        MainPage = new MainPage();
#if FACE_VERIFICATION && ANDROID
        MainPage.Loaded += async (_, _) => await AndroidVerification.RunAsync();
#endif
#if FACE_VERIFICATION && WINDOWS
        MainPage.Loaded += async (_, _) => await DesktopVerification.RunAsync();
#endif
    }
}
