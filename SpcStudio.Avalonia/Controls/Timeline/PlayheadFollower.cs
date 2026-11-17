using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace SpcStudio.Controls;

// Keeps a playback line in a scroll viewer's view, scrolling it to the view's left edge where playback has taken it out of view, and leaving the view
// alone while the viewer's horizontal scroll bar is held.
public sealed class PlayheadFollower
{
    private const string HorizontalScrollBarName = "PART_HorizontalScrollBar";

    private readonly ScrollViewer _viewer;
    private ScrollBar? _scrollBar;
    private RepeatButton[]? _scrollBarTrackButtons;
    private bool _isScrollBarThumbDragged;

    // Follows playback in a scroll viewer.
    public PlayheadFollower(ScrollViewer viewer)
    {
        _viewer = viewer;
    }

    // Scrolls a line at a horizontal position in the viewer's content to the view's left edge where it is out of view, unless the scroll bar is held.
    public void KeepInView(double lineX)
    {
        if (IsScrollBarHeld())
        {
            return;
        }

        double offsetX = _viewer.Offset.X;
        if (lineX >= offsetX && lineX <= offsetX + _viewer.Viewport.Width)
        {
            return;
        }

        _viewer.Offset = _viewer.Offset.WithX(lineX);
    }

    // A scroll bar's track buttons, or null until its template has made them.
    private static RepeatButton[]? TrackButtonsOf(ScrollBar bar)
    {
        var buttons = bar.GetVisualDescendants().OfType<RepeatButton>().ToArray();
        return buttons.Length > 0 ? buttons : null;
    }

    // Whether the viewer's horizontal scroll bar is scrolling the view under the pointer: its thumb dragged or one of its track buttons held.
    private bool IsScrollBarHeld()
    {
        if (HookedScrollBar() is not { } bar)
        {
            return false;
        }

        if (_isScrollBarThumbDragged)
        {
            return true;
        }

        _scrollBarTrackButtons ??= TrackButtonsOf(bar);
        return _scrollBarTrackButtons?.Any(button => button.IsPressed) == true;
    }

    // The viewer's horizontal scroll bar with its thumb's drag followed, found the first time it is asked for, or null until the viewer's template has made it.
    private ScrollBar? HookedScrollBar()
    {
        if (_scrollBar is not null)
        {
            return _scrollBar;
        }

        _scrollBar = _viewer.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(bar => bar.Name == HorizontalScrollBarName);
        _scrollBar?.AddHandler(Thumb.DragStartedEvent, (_, _) => _isScrollBarThumbDragged = true);
        _scrollBar?.AddHandler(Thumb.DragCompletedEvent, (_, _) => _isScrollBarThumbDragged = false);
        return _scrollBar;
    }
}
