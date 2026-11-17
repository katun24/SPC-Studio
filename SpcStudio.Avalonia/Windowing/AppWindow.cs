using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SpcStudio.Controls;
using SpcStudio.Theme;

namespace SpcStudio;

// Every window of the app: its title bar darkened, its keys heard ahead of whatever has the focus, closed by a bare Escape unless it is the main
// window, a field's focus dropped by a press nothing answers, and a close held back while the window has a question to ask first.
public abstract class AppWindow : Window
{
    private bool _isCloseConfirmed;
    private bool _hasFocusMovedDuringPress;

    // Darkens the title bar, hooks the window's key handlers ahead of every control in it, and hooks the press handlers that drop a field's focus.
    protected AppWindow()
    {
        DarkTitleBar.Apply(this);
        AddHandler(KeyDownEvent, (_, e) => OnKeyDownInWindow(e), RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => HandleWindowKeyUp(e), RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, _) => _hasFocusMovedDuringPress = false, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, _) => _hasFocusMovedDuringPress = true, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, e) => ClearFieldFocusOnUnansweredPress(e), RoutingStrategies.Bubble);
    }

    // Whether a bare Escape closes the window, which it does in every window but the main one.
    protected virtual bool IsClosedByEscape
    {
        get
        {
            return true;
        }
    }

    // Whether closing the window has a question to ask first.
    protected virtual bool HasCloseQuestion
    {
        get
        {
            return false;
        }
    }

    // Shows a secondary window centered on this one by its frame, and owned by nothing.
    public void ShowBeside(Window window)
    {
        var frameSize = FrameSize ?? ClientSize;
        var chromeSize = frameSize - ClientSize;
        var windowFrameSize = new Size(window.Width + chromeSize.Width, window.Height + chromeSize.Height);
        window.Position = WindowPlacement.CenteredOn(Position, frameSize, windowFrameSize, RenderScaling);
        window.Show();
    }

    // Asks the window's question before it closes, answering whether it may, and lets it close without asking again where it may.
    public async Task<bool> ConfirmClose()
    {
        if (HasCloseQuestion && !await AskCloseQuestion())
        {
            return false;
        }

        _isCloseConfirmed = true;
        return true;
    }

    // Closes the window without asking its question, the caller having asked what it needed to already.
    protected void CloseWithoutAsking()
    {
        _isCloseConfirmed = true;
        Close();
    }

    // Holds a close back where the window has a question to ask first, to ask it, and otherwise does what the window does as it closes, unless the close was called off.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (!_isCloseConfirmed && HasCloseQuestion)
        {
            e.Cancel = true;
            ConfirmThenClose();
            return;
        }

        if (e.Cancel)
        {
            return;
        }

        OnCloseGoingAhead();
    }

    // Does what the window does once a close is going ahead.
    protected virtual void OnCloseGoingAhead()
    {
    }

    // Asks the window's question before it closes, answering whether it may.
    protected virtual Task<bool> AskCloseQuestion()
    {
        return Task.FromResult(true);
    }

    // Takes the keyboard's focus off an element of another window or of a closed one once this window is active, so the keys pressed here come here.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsActiveProperty || !IsActive || !IsFocusElsewhere(FocusManager?.GetFocusedElement()))
        {
            return;
        }

        FocusManager?.ClearFocus();
    }

    // Answers a key going down anywhere in the window, ahead of whatever has the focus, bar the Escape that closes it.
    protected virtual void HandleWindowKeyDown(KeyEventArgs e)
    {
    }

    // Answers a key coming up anywhere in the window, ahead of whatever has the focus.
    protected virtual void HandleWindowKeyUp(KeyEventArgs e)
    {
    }

    // Closes the window on a bare Escape, leaving it to a rename field that has the focus, and hands every other key to the window's own keys.
    private void OnKeyDownInWindow(KeyEventArgs e)
    {
        if (!IsClosedByEscape || !WindowKeys.IsBareEscape(e) || RenameField.TakesEscape(FocusManager?.GetFocusedElement()))
        {
            HandleWindowKeyDown(e);
            KeepTabFromMovingFocus(e);
            return;
        }

        Close();
        e.Handled = true;
    }

    // Keeps Tab, with or without a modifier, from moving the focus from one control to the next.
    private static void KeepTabFromMovingFocus(KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            e.Handled = true;
        }
    }

    // Whether the focused element stands in another window or in one that has closed, a pop-up of this window's counting as this window.
    private bool IsFocusElsewhere(IInputElement? focused)
    {
        return focused is Visual element && ElementWindow.Of(element) != this;
    }

    // Takes the focus out of a field when a left or right press outside it reached the window with nothing having answered it or moved the focus.
    private void ClearFieldFocusOnUnansweredPress(PointerPressedEventArgs e)
    {
        var focused = FocusManager?.GetFocusedElement();
        if (focused is not TextBox field || _hasFocusMovedDuringPress || !PointerButtons.IsLeftOrRightHeld(e.GetCurrentPoint(this).Properties))
        {
            return;
        }

        if (e.Source is Visual pressed && pressed.FindAncestorOfType<TextBox>(includeSelf: true) == field)
        {
            return;
        }

        FocusManager?.ClearFocus();
    }

    // Asks the question the held-back close could not wait for, and closes again where the answer allows it.
    private async void ConfirmThenClose()
    {
        if (await ConfirmClose())
        {
            Close();
        }
    }
}
