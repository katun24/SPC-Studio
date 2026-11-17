using Avalonia.Controls;
using SpcStudio.Controls;
using SpcStudio.Export;

namespace SpcStudio;

// The two paths Preferences and the launch's prompt pick or take typed - the AddmusicK install and the ROM - and the one way a row takes either.
public static class PathPicks
{
    // The AddmusicK install: a folder AddmusicK can be run in, picked starting at the one shown.
    public static readonly PathPick AmkFolder = new(WindowTitles.Failures.InvalidAmkFolder, directory => AmkFolderCheck.RefusalOf(directory)?.Message,
        (owner, shownDirectory) => FilePickers.PickFolder(owner, WindowTitles.SelectAmkFolderPicker, PickerId.AmkFolder, shownDirectory));

    // The ROM: a SNES ROM that is there, picked starting beside the one set.
    public static readonly PathPick Rom = new(WindowTitles.Failures.InvalidRom, romPath => RomFile.IsRom(romPath) ? null : QuestionsAndNotices.NotValidRom(romPath),
        (owner, _) => FilePickers.OpenFile(owner, WindowTitles.SelectRomPicker, RomFile.DialogFilter, PickerId.RomFile, AppPaths.RomFolder));

    // Makes a row in a window pick with its Browse button and take what is typed into it, reporting each path taken and null where the field was emptied.
    public static void Wire(Window owner, PathPickRow row, PathPick pick, Action<string?> reportChosen)
    {
        row.BrowseClick += async (_, _) => await PickAndTake(owner, row, pick, reportChosen);
        row.PathTyped += async typedPath => await TakeTyped(owner, row, pick, typedPath, reportChosen);
    }

    // Opens the row's picker and takes the path picked where it passes.
    private static async Task PickAndTake(Window owner, PathPickRow row, PathPick pick, Action<string?> reportChosen)
    {
        if (await pick.PickFrom(owner, row.ShownPath) is not { } pickedPath)
        {
            return;
        }

        await TakeIfUsable(owner, row, pick, pickedPath, reportChosen);
    }

    // Clears the row's path where its field was emptied, and otherwise takes the path typed where it passes.
    private static async Task TakeTyped(Window owner, PathPickRow row, PathPick pick, string typedPath, Action<string?> reportChosen)
    {
        if (typedPath.Length == 0)
        {
            row.ShownPath = null;
            reportChosen(null);
            return;
        }

        await TakeIfUsable(owner, row, pick, typedPath, reportChosen);
    }

    // Shows and reports a path where it passes, and otherwise puts the row's own path back in its field and says in a box why the path was refused.
    private static async Task TakeIfUsable(Window owner, PathPickRow row, PathPick pick, string path, Action<string?> reportChosen)
    {
        if (pick.RefusalOf(path) is { } refusal)
        {
            row.ShowPathAgain();
            await MessageDialog.Show(owner, refusal, pick.RefusalTitle);
            return;
        }

        row.ShownPath = path;
        reportChosen(path);
    }
}
