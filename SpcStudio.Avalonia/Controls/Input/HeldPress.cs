using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SpcStudio.Controls;

// A button that sounds something for as long as it is held: one action on the press, and another on the release or on the capture being lost.
public static class HeldPress
{
    // Runs one action when the button is pressed and another when it is released or loses the pointer's capture.
    public static void HookPressAndRelease(Button button, Action onPress, Action onRelease)
    {
        button.AddHandler(InputElement.PointerPressedEvent, (_, _) => onPress(), RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => onRelease(), RoutingStrategies.Tunnel);
        button.PointerCaptureLost += (_, _) => onRelease();
    }
}
