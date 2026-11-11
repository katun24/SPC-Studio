using Avalonia;

namespace SpcStudio;

// The entry point, which builds the app and starts it with a desktop lifetime.
internal static class Program
{
    // Starts the app on a thread the clipboard and the shell dialogs can run on, once its native libraries can be found in their own folder.
    [STAThread]
    public static void Main(string[] args)
    {
        AppLibraries.DeleteCopiesBesideExe();
        AppLibraries.FindInLibrariesFolder();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Configures the app on the platform it finds itself on.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect();
    }
}
