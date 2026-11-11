using Avalonia.Controls;
using Avalonia.Input;
using SpcStudio.Controls;

namespace SpcStudio;

// The song list window's selected row, one in each table, and the drag that moves a row's song up or down its table, the rows it passes moving one row the other way and every row keeping its number.
public partial class SongListWindow
{
    private int _dragStartNumber;
    private int _dropNumber;
    private readonly ListRowDrag _rowDrag;

    // Makes a left press on a row select it, and a single one drag the row's song until the row lets the pointer go.
    private void MakeDraggable(Border view, int number)
    {
        view.PointerPressed += (_, e) => OnRowPressed(view, number, e);
        _rowDrag.Hook(view);
    }

    // Selects a row on a left press and, on a single one, starts dragging its song.
    private void OnRowPressed(Border view, int number, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(view).Properties.IsLeftButtonPressed || view.Parent is not StackPanel { Parent: ScrollViewer scroll } rowsPanel)
        {
            return;
        }

        Select(view);
        if (e.ClickCount != 1)
        {
            return;
        }

        _dragStartNumber = number;
        _dropNumber = number;
        _rowDrag.Start(view, scroll, rowsPanel, e);
    }

    // Shows the dragged song on the row standing at a height in its table, nearest the top or the bottom row where the height is past them, and selects that row.
    private void MoveDraggedSongTo(double heightInRows)
    {
        if (_rowDrag.DraggedRow?.Parent is not StackPanel rowsPanel)
        {
            return;
        }

        var entries = EntriesOf(rowsPanel, _history.Current);

        int rowIndexAtHeight = Math.Clamp((int)Math.Floor(heightInRows / ListRows.RowHeight), 0, entries.Count - 1);
        int dropNumber = entries[rowIndexAtHeight].Number;
        if (dropNumber == _dropNumber)
        {
            return;
        }

        _dropNumber = dropNumber;
        ShowTable(rowsPanel, _history.Current.WithSongMoved(_dragStartNumber, dropNumber));
        Select(ViewsOf(rowsPanel)[dropNumber].View);
    }

    // Takes a dragged song's move as one undo step once the row it was dragged from has let the pointer go.
    private void EndDrag()
    {
        Apply(_history.Current.WithSongMoved(_dragStartNumber, _dropNumber));
    }

    // Marks a row as its table's selected one, taking the mark off the row that had it.
    private static void Select(Border view)
    {
        if (view.Parent is not Panel rowsPanel)
        {
            return;
        }

        foreach (var row in rowsPanel.Children.OfType<Border>())
        {
            ListRows.ShowSelected(row, row == view);
        }
    }
}
