using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SpcStudio;

// Whether Shift is held in a window, followed through the keys the window hears and let go of as the window is left, its owner told each time.
public sealed class HeldShiftKey
{
    private readonly Action _showShiftHeld;

    // Follows Shift through a window's keys, telling the owner to show whether it is held as Shift goes down or up and as the window is left.
    public HeldShiftKey(Window window, Action showShiftHeld)
    {
        _showShiftHeld = showShiftHeld;
        FollowKeysOf(window);
        window.Deactivated += (_, _) => SetHeld(false);
    }

    public bool IsHeld { get; private set; }

    // Follows Shift through the keys a window hears as well, ahead of whatever has the focus there, without letting go of it as that window is left.
    public void FollowKeysOf(Window window)
    {
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => TakeKeyDown(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.KeyUpEvent, (_, e) => TakeKeyUp(e), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    // Notices Shift going down among the keys a window hears.
    private void TakeKeyDown(KeyEventArgs e)
    {
        if (IsShiftKey(e.Key))
        {
            SetHeld(true);
        }
    }

    // Notices Shift coming up among the keys a window hears.
    private void TakeKeyUp(KeyEventArgs e)
    {
        if (IsShiftKey(e.Key))
        {
            SetHeld(false);
        }
    }

    // Whether a key is either Shift key.
    private static bool IsShiftKey(Key key)
    {
        return key is Key.LeftShift or Key.RightShift;
    }

    // Writes whether Shift is held and tells the owner.
    private void SetHeld(bool isHeld)
    {
        IsHeld = isHeld;
        _showShiftHeld();
    }
}
