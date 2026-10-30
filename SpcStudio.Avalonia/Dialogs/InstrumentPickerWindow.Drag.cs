using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SpcStudio.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio;

// The Instrument Picker's drag: an instrument or a folder carried onto another folder, or into the instruments folder itself, and moved there on
// the release, the folder it would land in lit while it is held - its row, or the whole list for the instruments folder itself.
public partial class InstrumentPickerWindow
{
    private readonly ListRowDrag _rowDrag;
    private string? _dropFolderPath;

    // The row being dragged and what it stands for, from the press until the move is made.
    private Border? _draggedRow;
    private InstrumentLibraryEntry? _draggedEntry;

    // What is lit as where the drag would land - a folder's row, or the whole list for the instruments folder itself - or null where nothing is.
    private Control? _litDropTarget;

    // Starts dragging a row on a single press.
    private void StartDragOnSinglePress(Border row, InstrumentLibraryEntry entry, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 1)
        {
            return;
        }

        _draggedRow = row;
        _draggedEntry = entry;
        _dropFolderPath = null;
        _rowDrag.Start(row, TreeList.Scroll, TreeList.RowsPanel, e);
    }

    // The folder a release at a position over the tree would move the dragged entry into: the folder row under the pointer, the folder an instrument row under it stands in, or the instruments folder
    // below the last row; null outside the tree, and where the entry would not move or would go into itself.
    private string? DropFolderAt(Point overRows, Point overScrollViewer)
    {
        if (_draggedEntry is not { } dragged || !new Rect(TreeList.Scroll.Bounds.Size).Contains(overScrollViewer))
        {
            return null;
        }

        string folderPath = AppPaths.InstrumentsDirectory;
        if (EntryAt(overRows) is { } entry)
        {
            folderPath = entry.IsFolder ? entry.Path : Path.GetDirectoryName(entry.Path) ?? folderPath;
        }

        bool isAlreadyThere = string.Equals(Path.GetDirectoryName(dragged.Path), folderPath, StringComparison.OrdinalIgnoreCase);
        bool isIntoItself = dragged.IsFolder && FolderPaths.RelativePathUnder(dragged.Path, folderPath) is not null;
        return isAlreadyThere || isIntoItself ? null : folderPath;
    }

    // What the row at a position over the tree stands for, or null where no row is there.
    private InstrumentLibraryEntry? EntryAt(Point overRows)
    {
        for (var visual = TreeList.RowsPanel.InputHitTest(overRows) as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Border row && _entriesByRow.TryGetValue(row, out var entry))
            {
                return entry;
            }
        }

        return null;
    }

    // Takes the folder a release would move into, lighting its row - or the whole list for the instruments folder itself - and putting out what was lit before.
    private void ShowDropFolder(string? folderPath)
    {
        _dropFolderPath = folderPath;
        var target = DropTargetOf(folderPath);
        if (target == _litDropTarget)
        {
            return;
        }

        _litDropTarget?.Classes.Remove(ThemeClasses.DropTarget);
        target?.Classes.Add(ThemeClasses.DropTarget);
        _litDropTarget = target;
    }

    // What lights up for a drop into a folder: the folder's row, or the whole list for the instruments folder itself; nothing where there is no folder.
    private Control? DropTargetOf(string? folderPath)
    {
        if (folderPath is null)
        {
            return null;
        }

        if (string.Equals(folderPath, AppPaths.InstrumentsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return TreeList.Overlay;
        }

        return _entriesByRow.FirstOrDefault(pair => pair.Value.IsFolder && pair.Value.Path == folderPath).Key;
    }

    // Moves the dragged entry into the folder the release landed on once the dragged row has let the pointer go, as an undo step of its own.
    private async void EndDrag()
    {
        var dragged = _draggedEntry;
        string? dropFolderPath = _dropFolderPath;
        _draggedRow = null;
        _draggedEntry = null;
        ShowDropFolder(null);
        if (dragged is not null && dropFolderPath is not null)
        {
            var failureTitle = dragged.IsFolder ? WindowTitles.Failures.FailedToMoveFolder : WindowTitles.Failures.FailedToMoveInstrument;
            await Perform(new InstrumentLibraryChange(dragged.Path, InstrumentLibrary.MovedPath(dragged.Path, dropFolderPath)), failureTitle);
        }

        PostWaitingRebuild();
    }
}
