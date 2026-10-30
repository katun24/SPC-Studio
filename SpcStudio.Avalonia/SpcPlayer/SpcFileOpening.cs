using Avalonia.Controls;
using SpcStudio.Import;

namespace SpcStudio;

// The picking and reading of an .spc, which the main window and the SPC Player both do.
public static class SpcFileOpening
{
    // Picks an .spc in a file dialog over a window and reads it, answering the file and its path, or null where none was picked or it cannot be read, saying in a box why not.
    public static async Task<(string Path, SpcFile File)?> PickAndRead(Window owner, string pickerTitle)
    {
        string? chosenPath = await FilePickers.OpenFile(owner, pickerTitle, SpcReader.DialogFilter, PickerId.OpenSpc, null);
        if (chosenPath is null || await ReadOrSay(owner, chosenPath) is not { } file)
        {
            return null;
        }

        return (chosenPath, file);
    }

    // Reads an .spc, or says in a box over a window why it cannot be read and answers null.
    private static async Task<SpcFile?> ReadOrSay(Window owner, string path)
    {
        try
        {
            return SpcReader.Read(path);
        }
        catch (Exception ex) when (ex is SpcReadException or IOException)
        {
            await MessageDialog.Show(owner, ex.Message, WindowTitles.Failures.FailedToOpenSpc);
            return null;
        }
    }
}
