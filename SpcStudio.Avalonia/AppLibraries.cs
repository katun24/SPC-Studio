using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace SpcStudio;

// The native libraries a published app keeps in a folder of their own beside the exe: where the app finds them, and the copies an older version left beside the exe.
public static class AppLibraries
{
    private const string LibrariesFolderMetadataKey = "LibrariesFolder";
    private const string UnixLibraryPrefix = "lib";
    private const string WindowsLibraryExtension = ".dll";
    private const string MacLibraryExtension = ".dylib";
    private const string LinuxLibraryExtension = ".so";

    // The folder the published app's native libraries are in, its name set by the build.
    private static readonly string LibrariesFolder = Path.Combine(AppPaths.AppFolder, typeof(AppLibraries).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == LibrariesFolderMetadataKey).Value!);

    // This system's file extension for a native library.
    private static string LibraryExtension
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsLibraryExtension;
            }

            return OperatingSystem.IsMacOS() ? MacLibraryExtension : LinuxLibraryExtension;
        }
    }

    // Has every native library the app's own folder does not hold looked for in the libraries folder.
    public static void FindInLibrariesFolder()
    {
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += LoadFromLibrariesFolder;
    }

    // Deletes each file beside the exe that the libraries folder holds too, which an older version kept there, letting a failure pass.
    public static void DeleteCopiesBesideExe()
    {
        if (!Directory.Exists(LibrariesFolder))
        {
            return;
        }

        foreach (string libraryPath in Directory.GetFiles(LibrariesFolder))
        {
            try
            {
                File.Delete(Path.Combine(AppPaths.AppFolder, Path.GetFileName(libraryPath)));
            }
            catch (Exception)
            {
            }
        }
    }

    // Loads a native library from the libraries folder under any of the file names it can have, or answers no library where none is there.
    private static IntPtr LoadFromLibrariesFolder(Assembly requestingAssembly, string libraryName)
    {
        string[] fileNames = [libraryName, libraryName + LibraryExtension, UnixLibraryPrefix + libraryName + LibraryExtension];
        foreach (string fileName in fileNames)
        {
            if (NativeLibrary.TryLoad(Path.Combine(LibrariesFolder, fileName), out IntPtr libraryHandle))
            {
                return libraryHandle;
            }
        }

        return IntPtr.Zero;
    }
}
