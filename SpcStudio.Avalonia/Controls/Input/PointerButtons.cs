using Avalonia.Input;

namespace SpcStudio.Controls;

// What the mouse buttons are doing, asked the same way everywhere.
public static class PointerButtons
{
    // Whether the left, middle or right button is down.
    public static bool IsAnyHeld(PointerPointProperties buttons)
    {
        return buttons.IsLeftButtonPressed || buttons.IsMiddleButtonPressed || buttons.IsRightButtonPressed;
    }

    // Whether the left or right button is down.
    public static bool IsLeftOrRightHeld(PointerPointProperties buttons)
    {
        return buttons.IsLeftButtonPressed || buttons.IsRightButtonPressed;
    }

    // Whether a press is the right button's own, not any press while it happens to be held.
    public static bool IsRightPress(PointerPressedEventArgs e)
    {
        return e.GetCurrentPoint(null).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed;
    }
}
