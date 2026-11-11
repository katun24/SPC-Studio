using System.Reflection;

namespace SpcStudio;

// The version of the app that is running, as the project file writes it.
public static class AppVersion
{
    // The version exactly as the project file writes it, or nothing where the build carries none.
    public static string Text
    {
        get
        {
            return typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        }
    }
}
