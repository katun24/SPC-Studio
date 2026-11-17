using System.IO;

namespace SpcStudio.Model;

// The instruments folder as a tree of folders and instrument files: what a folder holds in the order it is shown, and the paths a rename, a move, a copy and a new folder lead to.
public static class InstrumentLibrary
{
    private const string NumberedFolderNamePrefix = "Folder";

    // A folder's subfolders and its instrument files, each in name order.
    public static (string[] SubfolderPaths, string[] InstrumentPaths) FolderContents(string folderPath)
    {
        var subfolderPaths = Directory.GetDirectories(folderPath).OrderBy(Path.GetFileName, NameOrder.Instance).ToArray();
        var instrumentPaths = Directory.GetFiles(folderPath)
            .Where(path => FileExtension.Matches(path, InstrumentFile.Extension))
            .OrderBy(InstrumentFile.NameOf, NameOrder.Instance)
            .ToArray();
        return (subfolderPaths, instrumentPaths);
    }

    // Every instrument file in a folder and in its subfolders, in the order the tree shows them.
    public static List<string> InstrumentPathsUnder(string folderPath)
    {
        var (subfolderPaths, instrumentPaths) = FolderContents(folderPath);
        var paths = new List<string>();
        foreach (string subfolderPath in subfolderPaths)
        {
            paths.AddRange(InstrumentPathsUnder(subfolderPath));
        }

        paths.AddRange(instrumentPaths);
        return paths;
    }

    // What an entry of the tree is called in a message: a folder, or an instrument.
    public static NamedThing ThingOf(bool isFolder)
    {
        return isFolder ? NamedThing.Folder : NamedThing.Instrument;
    }

    // The name an entry of the tree shows: a folder's own name, or an instrument's.
    public static string NameOf(string path, bool isFolder)
    {
        return isFolder ? Path.GetFileName(path) : InstrumentFile.NameOf(path);
    }

    // Where an entry of the tree goes when it is renamed, in its own folder.
    public static string RenamedPath(string path, bool isFolder, string name)
    {
        string folderPath = FolderOf(path);
        return isFolder ? Path.Combine(folderPath, name) : InstrumentFile.PathFor(folderPath, name);
    }

    // Where a copy of an entry of the tree goes: beside it, under the first copy-numbered version of its name its folder does not hold.
    public static string DuplicatePath(InstrumentLibraryEntry entry)
    {
        string folderPath = FolderOf(entry.Path);
        string extension = entry.IsFolder ? "" : InstrumentFile.Extension;
        return Path.Combine(folderPath, FreeNames.FirstFreeName(NameOf(entry.Path, entry.IsFolder), extension, name => IsNameTakenIn(folderPath, name)));
    }

    // Where an entry of the tree goes when it is moved into a folder, under its own name.
    public static string MovedPath(string path, string folderPath)
    {
        return Path.Combine(folderPath, Path.GetFileName(path));
    }

    // The path of a new folder in a folder, under the first numbered folder name the folder does not hold.
    public static string NewFolderPath(string parentPath)
    {
        string name = FreeNames.FirstFreeNumberedName(NumberedFolderNamePrefix, "", name => IsNameTakenIn(parentPath, name));
        return Path.Combine(parentPath, name);
    }

    // Where a path stands once an entry has moved, where the path is that entry or stands under it, and null otherwise.
    public static string? PathAfterMove(string path, string fromPath, string toPath)
    {
        return FolderPaths.RelativePathUnder(fromPath, path) is { } relativePath ? Path.Combine(toPath, relativePath) : null;
    }

    // The folder an entry of the tree stands in.
    private static string FolderOf(string path)
    {
        return Path.GetDirectoryName(path) ?? "";
    }

    // Whether a file or a folder of a name already stands in a folder.
    private static bool IsNameTakenIn(string folderPath, string name)
    {
        return Path.Exists(Path.Combine(folderPath, name));
    }
}
