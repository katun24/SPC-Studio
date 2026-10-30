using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SpcStudio;

// The app's file and folder dialogs, each taking a title, a filter and a folder to start in and answering a path or nothing.
internal static class FilePickers
{
    private const char FilterPartSeparator = '|';
    private const char PatternSeparator = ';';
    private const int PartsPerFileType = 2;
    private const string Win32AllFilesPattern = "*.*";
    private const string AllFilesPattern = "*";
    private const char ExtensionDot = '.';

    // Picks one existing file, or nothing.
    public static async Task<string?> OpenFile(Window owner, string title, string filter, string pickerId, string? defaultFolder)
    {
        var files = await PickExistingFiles(owner, title, filter, pickerId, defaultFolder, allowsSeveral: false);
        return AnsweredFile(owner, pickerId, FirstLocalPath(files));
    }

    // Picks any number of existing files, or none, in the order the picker hands them back, leaving out any that are not on a disk.
    public static async Task<IReadOnlyList<string>> OpenFiles(Window owner, string title, string filter, string pickerId, string? defaultFolder)
    {
        var files = await PickExistingFiles(owner, title, filter, pickerId, defaultFolder, allowsSeveral: true);
        List<string> pickedLocalPaths = files.Select(file => file.TryGetLocalPath()).Where(path => !string.IsNullOrEmpty(path)).Select(path => path!).ToList();
        RememberFolderOfFile(pickerId, pickedLocalPaths.FirstOrDefault());
        return WithFocusHandedBack<IReadOnlyList<string>>(owner, pickedLocalPaths);
    }

    // Picks where to write a file, or nothing, a name typed with no extension taking the one given.
    public static async Task<string?> SaveFile(Window owner, string title, string filter, string suggestedName, string extension, string pickerId, string? defaultFolder)
    {
        var storage = owner.StorageProvider;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = ParseWin32Filter(filter),
            DefaultExtension = extension.TrimStart(ExtensionDot),
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await StartFolderFor(storage, pickerId, defaultFolder),
        });

        return AnsweredFile(owner, pickerId, file?.TryGetLocalPath());
    }

    // Picks one folder, or nothing, remembering the picked folder itself as where the picker opens next.
    public static async Task<string?> PickFolder(Window owner, string title, string pickerId, string? defaultFolder)
    {
        var storage = owner.StorageProvider;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolderFor(storage, pickerId, defaultFolder),
        });

        string? path = FirstLocalPath(folders);
        AppPaths.RememberFolder(pickerId, path);
        return WithFocusHandedBack(owner, path);
    }

    // The existing files a picker hands back, shown with its title, its filter and the folder it starts in, letting one or several be picked.
    private static async Task<IReadOnlyList<IStorageFile>> PickExistingFiles(Window owner, string title, string filter, string pickerId, string? defaultFolder, bool allowsSeveral)
    {
        var storage = owner.StorageProvider;
        return await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowsSeveral,
            FileTypeFilter = ParseWin32Filter(filter),
            SuggestedStartLocation = await StartFolderFor(storage, pickerId, defaultFolder),
        });
    }

    // Remembers the folder a picked file sits in and hands the focus back to the window the dialog was opened from, answering the file's path.
    private static string? AnsweredFile(Window owner, string pickerId, string? path)
    {
        RememberFolderOfFile(pickerId, path);
        return WithFocusHandedBack(owner, path);
    }

    // Hands the focus back to the window the dialog was opened from, and answers what was picked.
    private static T WithFocusHandedBack<T>(Window owner, T picked)
    {
        owner.Activate();
        return picked;
    }

    // The local path of the first item picked, or null where nothing was picked or it is not on a disk.
    private static string? FirstLocalPath(IReadOnlyList<IStorageItem> picked)
    {
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    // Where one picker opens: the folder it was last used in, or its own default where there is none or that folder has gone.
    private static async Task<IStorageFolder?> StartFolderFor(IStorageProvider storage, string pickerId, string? defaultFolder)
    {
        return await StorageFolderAt(storage, AppPaths.LastFolder(pickerId)) ?? await StorageFolderAt(storage, defaultFolder);
    }

    // The folder at a path to start a picker in, or null for wherever the platform would open where there is no path or no folder there.
    private static async Task<IStorageFolder?> StorageFolderAt(IStorageProvider storage, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return await storage.TryGetFolderFromPathAsync(path);
    }

    // Remembers the folder a picked file sits in, a cancelled picker moving nothing.
    private static void RememberFolderOfFile(string pickerId, string? path)
    {
        AppPaths.RememberFolder(pickerId, path is null ? null : Path.GetDirectoryName(path));
    }

    // Turns a Win32 filter string of name and pattern pairs into the file types a picker takes, dropping a trailing name with no patterns.
    private static List<FilePickerFileType> ParseWin32Filter(string filter)
    {
        string[] parts = filter.Split(FilterPartSeparator);
        var fileTypes = new List<FilePickerFileType>(parts.Length / PartsPerFileType);

        for (int nameIndex = 0; nameIndex + 1 < parts.Length; nameIndex += PartsPerFileType)
        {
            fileTypes.Add(new FilePickerFileType(parts[nameIndex])
            {
                Patterns = parts[nameIndex + 1]
                    .Split(PatternSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Select(pattern => pattern == Win32AllFilesPattern ? AllFilesPattern : pattern)
                    .ToList(),
            });
        }

        return fileTypes;
    }
}
