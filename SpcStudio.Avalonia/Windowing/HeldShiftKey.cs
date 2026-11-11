using Avalonia.Controls;
using Avalonia.Input;

namespace SpcStudio;

// Whether Shift is held in a window, followed through the keys the window hears and let go of as the window is left, its owner told each time.
public sealed class HeldShiftKey
{
    private readonly Action _showShiftHeld;

    // Follows Shift for a window, telling the owner to show whether it is held as Shift goes down or up and as the window is left.
    public HeldShiftKey(Window window, Action showShiftHeld)
    {
        _showShiftHeld = showShiftHeld;
        window.Deactivated += (_, _) => SetHeld(false);
    }

    public bool IsHeld { get; private set; }

    // Notices Shift going down among the keys a window hears.
    public void TakeKeyDown(KeyEventArgs e)
    {
        if (IsShiftKey(e.Key))
        {
            SetHeld(true);
        }
    }

    // Notices Shift coming up among the keys a window hears.
    public void TakeKeyUp(KeyEventArgs e)
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
