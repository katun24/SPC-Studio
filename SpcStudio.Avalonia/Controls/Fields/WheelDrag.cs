using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SpcStudio.Controls;

// A drag with the scroll wheel held down: the control pressed holds the pointer, and every movement is handed on until the wheel is let go.
public sealed class WheelDrag
{
    private readonly Control _control;
    private readonly Action<Vector> _onMoved;
    private readonly WindowCursor _windowCursor = new();

    // Starts following a drag from the press that began it.
    private WheelDrag(Control control, PointerPressedEventArgs e, Action<Vector> onMoved)
    {
        _control = control;
        _onMoved = onMoved;
        _windowCursor.Remember(control, e);
        _windowCursor.MarkPosition();
    }

    // Whether a press is the scroll wheel's own.
    public static bool IsWheelPress(PointerPressedEventArgs e)
    {
        return e.GetCurrentPoint(null).Properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed;
    }

    // Whether a press is the scroll wheel's with Shift held, which moves the playback marker rather than doing what the wheel button does alone.
    public static bool IsPlayheadPress(PointerPressedEventArgs e)
    {
        return IsWheelPress(e) && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
    }

    // Holds the pointer on a control from a wheel press and hands each movement after it, in the window's coordinates, to an action until the wheel is let go or the hold is lost.
    public static void Follow(Control control, PointerPressedEventArgs e, Action<Vector> onMoved)
    {
        var drag = new WheelDrag(control, e, onMoved);
        control.AddHandler(InputElement.PointerMovedEvent, drag.OnPointerMoved, RoutingStrategies.Tunnel);
        control.AddHandler(InputElement.PointerReleasedEvent, drag.OnPointerReleased, RoutingStrategies.Tunnel);
        control.PointerCaptureLost += drag.OnPointerCaptureLost;
        e.Pointer.Capture(control);
    }

    // Hands on how far the pointer has moved since the last movement.
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        _windowCursor.Remember(_control, e);
        Vector moved = _windowCursor.TakeMovementSinceMark();
        if (moved == default)
        {
            return;
        }

        _onMoved(moved);
    }

    // Lets the pointer go when the wheel is.
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Middle)
        {
            return;
        }

        e.Handled = true;
        e.Pointer.Capture(null);
    }

    // Stops following once the control no longer holds the pointer.
    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _control.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _control.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        _control.PointerCaptureLost -= OnPointerCaptureLost;
    }
}
