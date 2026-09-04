#if ANDROID
using CommunityToolkit.Maui;
#endif

namespace ExaTareo;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
#if ANDROID
        builder.UseMauiCommunityToolkitCamera();
#endif
        return builder.Build();
    }
}
