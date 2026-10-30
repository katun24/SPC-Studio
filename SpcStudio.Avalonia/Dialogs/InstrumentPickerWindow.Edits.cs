using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The Instrument Picker's changes to the instruments folder: renaming an instrument or a folder, adding a folder, duplicating and deleting either, each
// but the delete an undo step of the picker's own history, and that history's undo and redo.
public partial class InstrumentPickerWindow
{
    private TextBox? _renameBox;
    private string? _folderPathAwaitingRename;

    // Opens a row's rename where it is the one awaited.
    private void StartRenameIfAwaited(InstrumentLibraryEntry entry, DockPanel content, TextBlock name)
    {
        if (entry.Path != _folderPathAwaitingRename)
        {
            return;
        }

        _folderPathAwaitingRename = null;
        Dispatcher.UIThread.Post(() => StartRename(entry, content, name), DispatcherPriority.Input);
    }

    // Puts a field holding a row's name in place of the name, selected whole so it is typed over, where no other rename is open.
    private void StartRename(InstrumentLibraryEntry entry, DockPanel content, TextBlock name)
    {
        if (_renameBox is not null)
        {
            return;
        }

        var box = new TextBox { Text = name.Text, VerticalAlignment = VerticalAlignment.Center };
        FieldInput.SetAccepts(box, FieldChars.DiskName);
        RenameField.Wire(box, () => CommitRename(entry, box), EndRename);
        _renameBox = box;
        name.IsVisible = false;
        content.Children.Add(box);
        FieldInput.FocusField(box);
    }

    // Closes a rename and renames the file or folder to the name typed, where it differs from its own, as an undo step of its own, saying in a box why a name it cannot have is refused.
    private async void CommitRename(InstrumentLibraryEntry entry, TextBox box)
    {
        if (_renameBox != box)
        {
            return;
        }

        string name = DiskNameRules.Normalize(box.Text);
        EndRename();
        if (name == InstrumentLibrary.NameOf(entry.Path, entry.IsFolder))
        {
            return;
        }

        var failureTitle = entry.IsFolder ? WindowTitles.Failures.FailedToRenameFolder : WindowTitles.Failures.FailedToRenameInstrument;
        if (NameRefusals.OfDiskName(InstrumentLibrary.ThingOf(entry.IsFolder), name) is { } refusal)
        {
            await MessageDialog.Show(this, refusal, failureTitle);
            return;
        }

        await Perform(new InstrumentLibraryChange(entry.Path, InstrumentLibrary.RenamedPath(entry.Path, entry.IsFolder, name)), failureTitle);
    }

    // Takes an open rename's field away and shows the row's name again, posting the rebuild that waited for it.
    private void EndRename()
    {
        if (_renameBox is not { Parent: DockPanel content } box)
        {
            return;
        }

        _renameBox = null;
        content.Children.Remove(box);
        foreach (var name in content.Children.OfType<TextBlock>())
        {
            name.IsVisible = true;
        }

        PostWaitingRebuild();
    }

    // Adds a folder to the instruments folder under the first free numbered name, as an undo step of its own, and opens its rename once it is shown.
    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        await PerformAndAwaitRename(new InstrumentLibraryChange(null, InstrumentLibrary.NewFolderPath(AppPaths.InstrumentsDirectory)), WindowTitles.Failures.FailedToAddFolder);
    }

    // Copies a file, or a folder with everything in it, beside itself under the first free copy-numbered name, as an undo step of its own, and selects the copy and opens its rename once it is shown.
    private async void Duplicate(InstrumentLibraryEntry entry)
    {
        string copyPath = InstrumentLibrary.DuplicatePath(entry);
        var failureTitle = entry.IsFolder ? WindowTitles.Failures.FailedToDuplicateFolder : WindowTitles.Failures.FailedToDuplicateInstrument;
        if (!await PerformAndAwaitRename(new InstrumentLibraryChange(null, copyPath, entry), failureTitle))
        {
            return;
        }

        _selectedPath = copyPath;
    }

    // Makes a change that adds an entry, as an undo step of its own, and opens the new entry's rename once it is shown; answers whether it was made.
    private async Task<bool> PerformAndAwaitRename(InstrumentLibraryChange change, FailureTitle failureTitle)
    {
        if (!await Perform(change, failureTitle))
        {
            return false;
        }

        _folderPathAwaitingRename = change.ToPath;
        return true;
    }

    // Asks before deleting a file or a folder with everything in it, and deletes it where the answer is yes; a delete is no undo step.
    private async void Delete(InstrumentLibraryEntry entry)
    {
        try
        {
            string name = InstrumentLibrary.NameOf(entry.Path, entry.IsFolder);
            string deleteQuestion = entry.IsFolder
                ? QuestionsAndNotices.DeleteFolder(name, InstrumentLibrary.InstrumentPathsUnder(entry.Path).Count)
                : QuestionsAndNotices.DeleteInstrumentFile(name);
            var title = entry.IsFolder ? WindowTitles.Notices.DeleteFolder : WindowTitles.Notices.DeleteInstrument;
            if (await MessageDialog.Show(this, deleteQuestion, title, MessageButtons.YesNo) != MessageAnswer.Yes)
            {
                return;
            }

            DiskDelete.Delete(entry.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.Show(this, ex.Message, WindowTitles.Failures.FailedToDelete);
        }

        RestartRebuildTimer();
    }

    // Makes a change on disk and records it as an undo step, answering whether it was made.
    private async Task<bool> Perform(InstrumentLibraryChange change, FailureTitle failureTitle)
    {
        if (!await TryApply(change, failureTitle))
        {
            return false;
        }

        _history.Record(change);
        return true;
    }

    // Takes back the last change made here.
    private async void Undo()
    {
        if (_history.ChangeToUndo is not { } change)
        {
            return;
        }

        _history.Undo();
        var failureTitle = HistoryFailureTitle(
            change, WindowTitles.Failures.FailedToUndo, WindowTitles.Failures.FailedToUndoInstrumentDuplication, WindowTitles.Failures.FailedToUndoFolderDuplication);
        await TryApply(change.Reversed, failureTitle);
    }

    // Makes again the last change taken back.
    private async void Redo()
    {
        if (!_history.CanRedo || _history.Redo().LastChange is not { } change)
        {
            return;
        }

        var failureTitle = HistoryFailureTitle(
            change, WindowTitles.Failures.FailedToRedo, WindowTitles.Failures.FailedToRedoInstrumentDuplication, WindowTitles.Failures.FailedToRedoFolderDuplication);
        await TryApply(change, failureTitle);
    }

    // Makes a change on disk, carrying the selection and the open folders along with what moved and building the tree again once the disk has
    // settled, or says in a box why it cannot be made; answers whether it was made.
    private async Task<bool> TryApply(InstrumentLibraryChange change, FailureTitle failureTitle)
    {
        try
        {
            change.Apply();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.Show(this, ex.Message, failureTitle);
            return false;
        }

        FollowMove(change);
        RestartRebuildTimer();
        return true;
    }

    // The title over a failed undo or redo of a change: a duplication's own title, an instrument's or a folder's, where the change copies an entry, and the general title otherwise.
    private static FailureTitle HistoryFailureTitle(InstrumentLibraryChange change, FailureTitle generalTitle, FailureTitle instrumentTitle, FailureTitle folderTitle)
    {
        if (change.CopiedEntry is not { } copiedEntry)
        {
            return generalTitle;
        }

        return copiedEntry.IsFolder ? folderTitle : instrumentTitle;
    }

    // Carries the selected instrument and the open folders to where a move or a rename took them.
    private void FollowMove(InstrumentLibraryChange change)
    {
        if (change.FromPath is not { } fromPath || change.ToPath is not { } toPath)
        {
            return;
        }

        if (_selectedPath is not null && InstrumentLibrary.PathAfterMove(_selectedPath, fromPath, toPath) is { } movedSelection)
        {
            _selectedPath = movedSelection;
        }

        foreach (string folderKey in _openFolderKeys.ToList())
        {
            if (InstrumentLibrary.PathAfterMove(FolderPathOf(folderKey), fromPath, toPath) is not { } movedFolderPath)
            {
                continue;
            }

            _openFolderKeys.Remove(folderKey);
            _openFolderKeys.Add(FolderKeyOf(movedFolderPath));
        }
    }
}
