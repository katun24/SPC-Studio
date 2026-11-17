using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SpcStudio.Controls;

// A drag of a list row: the row holds the pointer from the press to the release, the list scrolls toward the pointer while it is past the list's top
// or bottom edge, and the window is told where the pointer stands at each move and when the drag ends.
public sealed class ListRowDrag(Action<Point, Point> followPointer, Action endDrag)
{
    private readonly WindowCursor _lastDragPointer = new();
    private Border? _draggedRow;

    // The scroll viewer around the list and the list's rows inside it, while a row is being dragged.
    private ScrollViewer? _scrollViewer;
    private Control? _rowsPanel;

    // The scroll that follows the drag past the list's top or bottom edge, while a row is being dragged.
    private EdgeScroll? _edgeScroll;

    // The row being dragged, or null where none is.
    public Border? DraggedRow
    {
        get
        {
            return _draggedRow;
        }
    }

    // Hands a row's pointer moves and the end of its hold on the pointer to the drag.
    public void Hook(Border row)
    {
        row.PointerMoved += (_, e) => OnRowPointerMoved(row, e);
        row.PointerCaptureLost += (_, _) => End(row);
    }

    // Starts dragging a row from a press on it, the row taking the pointer until it is let go.
    public void Start(Border row, ScrollViewer scrollViewer, Control rowsPanel, PointerPressedEventArgs e)
    {
        e.Pointer.Capture(row);
        _draggedRow = row;
        _scrollViewer = scrollViewer;
        _rowsPanel = rowsPanel;
        _edgeScroll = new EdgeScroll(new ScrollViewerView(scrollViewer), () => new Size(0, ListRows.RowHeight)) { FixedStepColumns = EdgeScroll.MinStepColumns };
        _lastDragPointer.Remember(row, e);
    }

    // Follows the pointer of the row being dragged.
    private void OnRowPointerMoved(Border row, PointerEventArgs e)
    {
        if (row != _draggedRow)
        {
            return;
        }

        _lastDragPointer.Remember(row, e);
        FollowCursor();
    }

    // Tells the window where the pointer stands over the rows and over the scroll viewer, and scrolls the list toward the pointer where it is past the
    // list's top or bottom edge, answering false where no row is being dragged.
    private bool FollowCursor()
    {
        if (_draggedRow is null || _scrollViewer is null || _rowsPanel is null || _edgeScroll is null)
        {
            return false;
        }

        if (_lastDragPointer.PositionOver(_rowsPanel) is not Point overRows || _lastDragPointer.PositionOver(_scrollViewer) is not Point overScrollViewer)
        {
            return false;
        }

        followPointer(overRows, overScrollViewer);
        _edgeScroll.FollowDrag(null, _scrollViewer.Offset.Y + overScrollViewer.Y, FollowCursor);
        return true;
    }

    // Ends the drag once the row being dragged has let the pointer go, and tells the window.
    private void End(Border row)
    {
        if (row != _draggedRow)
        {
            return;
        }

        _edgeScroll?.Stop();
        _edgeScroll = null;
        _draggedRow = null;
        _scrollViewer = null;
        _rowsPanel = null;
        endDrag();
    }
}
