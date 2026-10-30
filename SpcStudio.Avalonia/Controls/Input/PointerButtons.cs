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
}
