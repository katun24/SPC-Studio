using Avalonia.Controls;
using Avalonia.Input;

namespace SpcStudio.Controls;

// A keyboard's press on a key with one of the buttons it follows, held until that button comes up or the pointer's capture is taken away, the key it is
// on followed as it is dragged, and its end reported once by whichever route it comes.
public sealed class HeldKeyboardPress
{
    private readonly Control _keyboard;
    private readonly IReadOnlyCollection<MouseButton> _followedButtons;
    private readonly Action _onEnd;

    // Follows the presses of some buttons on a keyboard, running an action as each held press ends.
    public HeldKeyboardPress(Control keyboard, IReadOnlyCollection<MouseButton> followedButtons, Action onEnd)
    {
        _keyboard = keyboard;
        _followedButtons = followedButtons;
        _onEnd = onEnd;
        keyboard.PointerReleased += OnReleased;
        keyboard.PointerCaptureLost += (_, _) => End();
    }

    public bool IsHeld { get; private set; }
    public MouseButton HeldButton { get; private set; }
    public int HeldKey { get; private set; }

    // Whether a press can start a hold: one of the followed buttons', while none is held.
    public bool CanStart(PointerPressedEventArgs e)
    {
        return !IsHeld && _followedButtons.Contains(PressedButtonOf(e));
    }

    // Holds a press on a key, the keyboard taking the pointer until the release.
    public void Hold(PointerPressedEventArgs e, int key)
    {
        IsHeld = true;
        HeldButton = PressedButtonOf(e);
        HeldKey = key;
        e.Pointer.Capture(_keyboard);
    }

    // Moves the held press onto a key, answering whether it is another key than the one it was on.
    public bool HasMovedOnto(int key)
    {
        if (key == HeldKey)
        {
            return false;
        }

        HeldKey = key;
        return true;
    }

    // The button a press was made with.
    private MouseButton PressedButtonOf(PointerPressedEventArgs e)
    {
        return e.GetCurrentPoint(_keyboard).Properties.PointerUpdateKind.GetMouseButton();
    }

    // Lets the pointer go when the held press's button comes up, which ends it through the capture.
    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == HeldButton && IsHeld)
        {
            e.Pointer.Capture(null);
        }
    }

    // Ends a held press and reports it, where one is held.
    private void End()
    {
        if (!IsHeld)
        {
            return;
        }

        IsHeld = false;
        _onEnd();
    }
}
