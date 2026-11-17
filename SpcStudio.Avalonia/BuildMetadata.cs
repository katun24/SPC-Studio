using System.Reflection;

namespace SpcStudio;

// The values the project file hands the app when it is built, each read by its key.
public static class BuildMetadata
{
    // The value the build wrote under a key.
    public static string ValueOf(string key)
    {
        return typeof(BuildMetadata).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == key).Value!;
    }
}
