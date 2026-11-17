using Avalonia.Controls;
using SpcStudio.Import;

namespace SpcStudio;

// The picking and reading of an .spc, which the main window and the SPC Player both do.
public static class SpcFileOpening
{
    // Picks an .spc in a file dialog over a window and reads it, answering the file and its path, or null where none was picked or it cannot be read, saying in a box why not.
    public static async Task<(string Path, SpcFile File)?> PickAndRead(Window owner, string pickerTitle)
    {
        string? chosenPath = await PickPath(owner, pickerTitle);
        if (chosenPath is null || await ReadOrSay(owner, chosenPath, WindowTitles.Failures.FailedToOpenSpc) is not { } file)
        {
            return null;
        }

        return (chosenPath, file);
    }

    // Picks an .spc in a file dialog over a window, answering its path, or null where none was picked.
    public static Task<string?> PickPath(Window owner, string pickerTitle)
    {
        return OpenSpcPicker(FilePickers.OpenFile, owner, pickerTitle);
    }

    // Picks any number of .spc files in a file dialog over a window, answering their paths, none where none was picked.
    public static Task<IReadOnlyList<string>> PickPaths(Window owner, string pickerTitle)
    {
        return OpenSpcPicker(FilePickers.OpenFiles, owner, pickerTitle);
    }

    // Opens the .spc picker under a title over a window in a given way of picking.
    private static Task<T> OpenSpcPicker<T>(Func<Window, string, string, string, string?, Task<T>> open, Window owner, string pickerTitle)
    {
        return open(owner, pickerTitle, SpcReader.DialogFilter, PickerId.OpenSpc, null);
    }

    // Reads an .spc, or says in a box over a window under a failure title why it cannot be read and answers null.
    public static async Task<SpcFile?> ReadOrSay(Window owner, string path, FailureTitle failureTitle)
    {
        try
        {
            return SpcReader.Read(path);
        }
        catch (Exception ex) when (ex is SpcReadException or IOException)
        {
            await MessageDialog.Show(owner, ex.Message, failureTitle);
            return null;
        }
    }
}
