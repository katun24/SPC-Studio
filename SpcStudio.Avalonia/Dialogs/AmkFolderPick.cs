using Avalonia.Controls;
using SpcStudio.Controls;
using SpcStudio.Export;

namespace SpcStudio;

// The folder picker for the AddmusicK install, which Preferences and the launch's prompt both open.
public static class AmkFolderPick
{
    // Picks a folder starting at the one a row shows, showing it in the row and answering it where it is an install the app can run, and saying why and answering null where it is not.
    public static async Task<string?> PickUsableInto(Window owner, PathPickRow row)
    {
        string? directory = await FilePickers.PickFolder(owner, WindowTitles.SelectAmkFolderPicker, PickerId.AmkFolder, row.ShownPath);
        if (directory is null)
        {
            return null;
        }

        if (AmkFolderCheck.RefusalOf(directory) is { } refusal)
        {
            await MessageDialog.Show(owner, refusal.Message, WindowTitles.Failures.InvalidAmkFolder);
            return null;
        }

        row.ShownPath = directory;
        return directory;
    }
}
