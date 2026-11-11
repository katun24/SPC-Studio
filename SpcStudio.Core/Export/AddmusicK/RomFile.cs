using SpcStudio.Model;
using System.IO;

namespace SpcStudio.Export;

// What the app takes as the ROM AddmusicK inserts songs into: a file that is there and named as a SNES ROM is.
public static class RomFile
{
    private const string SmcExtension = ".smc";
    private const string SfcExtension = ".sfc";
    private const string ExtensionPatterns = "*" + SmcExtension + ";*" + SfcExtension;
    public const string DialogFilter = "SNES ROM (" + ExtensionPatterns + ")|" + ExtensionPatterns + DialogFilters.Separator + DialogFilters.AllFiles;

    // Whether a path names a file that is there and ends in one of a SNES ROM's two extensions.
    public static bool IsRom(string path)
    {
        return File.Exists(path) && (FileExtension.Matches(path, SmcExtension) || FileExtension.Matches(path, SfcExtension));
    }
}
