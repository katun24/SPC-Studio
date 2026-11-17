using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SpcStudio.Controls;

// Moving what a scroll viewer shows along with the pointer, held to what the viewer can scroll through; the scroll wheel's drag, which does it on every viewer that shows a
// scroll bar; and a wheel notch handed on to a viewer's own scrolling.
public static class ScrollPans
{
    // Gives every scroll viewer in the app that shows a bar the scroll wheel's drag, present and future.
    public static void WireWheelDragToScrollViewers()
    {
        InputElement.PointerPressedEvent.AddClassHandler<ScrollViewer>(OnPressed, RoutingStrategies.Bubble);
    }

    // Moves a viewer's content by a pointer movement, the content following the pointer, held to what the viewer can scroll through.
    public static void PanBy(ScrollViewer viewer, Vector moved)
    {
        double furthestX = Math.Max(0, viewer.Extent.Width - viewer.Viewport.Width);
        double furthestY = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        viewer.Offset = new Vector(Math.Clamp(viewer.Offset.X - moved.X, 0, furthestX), Math.Clamp(viewer.Offset.Y - moved.Y, 0, furthestY));
    }

    // Raises a wheel notch again on a viewer's presenter, as though it had come with the given keys held, and marks the original handled.
    public static void PassNotchTo(ScrollViewer viewer, PointerWheelEventArgs e, KeyModifiers heldKeys)
    {
        if (viewer.Presenter is not { } presenter || TopLevel.GetTopLevel(viewer) is not { } root)
        {
            return;
        }

        presenter.RaiseEvent(new PointerWheelEventArgs(presenter, e.Pointer, root, e.GetPosition(root), e.Timestamp, e.Properties, heldKeys, e.Delta)
        {
            RoutedEvent = InputElement.PointerWheelChangedEvent,
        });
        e.Handled = true;
    }

    // Starts a pan on a scroll wheel press that nothing inside the viewer took, where the viewer shows a bar it can scroll along.
    private static void OnPressed(ScrollViewer viewer, PointerPressedEventArgs e)
    {
        if (!WheelDrag.IsWheelPress(e) || !CanScrollAlongBar(viewer))
        {
            return;
        }

        e.Handled = true;
        WheelDrag.Follow(viewer, e, moved => PanBy(viewer, moved));
    }

    // Whether a viewer shows a bar along an axis it has more content on than room for.
    private static bool CanScrollAlongBar(ScrollViewer viewer)
    {
        return IsBarShown(viewer.HorizontalScrollBarVisibility) && viewer.Extent.Width > viewer.Viewport.Width
            || IsBarShown(viewer.VerticalScrollBarVisibility) && viewer.Extent.Height > viewer.Viewport.Height;
    }

    // Whether a bar's setting lets it be drawn.
    private static bool IsBarShown(ScrollBarVisibility visibility)
    {
        return visibility is ScrollBarVisibility.Auto or ScrollBarVisibility.Visible;
    }
}
