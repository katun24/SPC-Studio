using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SpcStudio.Controls;

// The pointer's last position, held in the window's coordinates so a control that scrolls under a still pointer can find where the pointer now is over it.
public sealed class WindowCursor
{
    private Point _markedInWindow;

    // The last position remembered, in the window's coordinates.
    public Point InWindow { get; private set; }

    // Remembers where a pointer event over a control happened, in the window's coordinates.
    public void Remember(Visual control, PointerEventArgs e)
    {
        if (TopLevel.GetTopLevel(control) is TopLevel window)
        {
            InWindow = e.GetPosition(window);
        }
    }

    // Marks the remembered position as the one the next movement is measured from.
    public void MarkPosition()
    {
        _markedInWindow = InWindow;
    }

    // How far the remembered position has moved since the mark, marking it again.
    public Vector TakeMovementSinceMark()
    {
        Vector movement = InWindow - _markedInWindow;
        _markedInWindow = InWindow;
        return movement;
    }

    // The remembered position in a control's own coordinates, or null where the control stands in no window.
    public Point? PositionOver(Visual control)
    {
        return TopLevel.GetTopLevel(control)?.TranslatePoint(InWindow, control);
    }

    // Hands where the cursor now is over a control to a gesture the control holds, and answers whether it holds one there.
    public bool FollowOver(Visual control, bool isGestureHeld, Action<Point> follow)
    {
        if (!isGestureHeld || PositionOver(control) is not Point cursor)
        {
            return false;
        }

        follow(cursor);
        return true;
    }
}
