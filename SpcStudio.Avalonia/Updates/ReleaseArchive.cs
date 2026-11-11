using System.Formats.Tar;
using System.IO;
using System.IO.Compression;

namespace SpcStudio;

// The kind of archive a release carries for the system the app runs on - a zip on Windows, a .tar.gz elsewhere - and how one is unpacked.
public static class ReleaseArchive
{
    private const string ZipExtension = ".zip";
    private const string TarGzExtension = ".tar.gz";

    private static readonly bool IsZipThisSystemsArchive = OperatingSystem.IsWindows();

    // The file ending of this system's download among a release's files.
    public static string Extension
    {
        get
        {
            return IsZipThisSystemsArchive ? ZipExtension : TarGzExtension;
        }
    }

    // Unpacks a downloaded archive of this system's kind into a folder, keeping each file's permissions where the system has them.
    public static void Extract(string archivePath, string folder)
    {
        if (IsZipThisSystemsArchive)
        {
            ZipFile.ExtractToDirectory(archivePath, folder);
            return;
        }

        Directory.CreateDirectory(folder);
        using var archive = File.OpenRead(archivePath);
        using var unpacked = new GZipStream(archive, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(unpacked, folder, overwriteFiles: false);
    }
}
