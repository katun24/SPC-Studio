using System.IO;

namespace SpcStudio.Model;

// Where a path stands against a folder.
public static class FolderPaths
{
    public const string ParentFolder = "..";
    public const string SameFolder = ".";

    // The path of a file or folder relative to a folder, where it is that folder or stands somewhere under it, and null where it stands elsewhere; the folder itself is the empty path.
    public static string? RelativePathUnder(string folderPath, string path)
    {
        string relativePath = Path.GetRelativePath(folderPath, path);
        if (relativePath == SameFolder)
        {
            return "";
        }

        bool isOutsideFolder = Path.IsPathRooted(relativePath) || relativePath == ParentFolder
            || relativePath.StartsWith(ParentFolder + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        return isOutsideFolder ? null : relativePath;
    }
}
