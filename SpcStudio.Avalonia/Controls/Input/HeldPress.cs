using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SpcStudio.Controls;

// A button that sounds something for as long as it is held: one action on the press, and another on the release or on the capture being lost. A right
// press on the list row or the drop-down's row the button is shown in sounds it the same way, for as long as the right button is held.
public static class HeldPress
{
    private static readonly ConditionalWeakTable<Button, HeldActions> ActionsOfButtons = [];
    private static readonly ConditionalWeakTable<Control, Action> ReleasesOfHeldRows = [];

    // Runs one action when the button is pressed and another when it is released or loses the pointer's capture.
    public static void HookPressAndRelease(Button button, Action onPress, Action onRelease)
    {
        ActionsOfButtons.AddOrUpdate(button, new HeldActions(onPress, onRelease));
        button.AddHandler(InputElement.PointerPressedEvent, (_, _) => onPress(), RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => onRelease(), RoutingStrategies.Tunnel);
        button.PointerCaptureLost += (_, _) => onRelease();
    }

    // Lets a right press on every drop-down's row in the app, present and future, sound the held button shown in it.
    public static void HookRightPressOnEveryDropDownRow()
    {
        InputElement.PointerPressedEvent.AddClassHandler<ComboBoxItem>(OnRowPressed, RoutingStrategies.Tunnel);
        InputElement.PointerReleasedEvent.AddClassHandler<ComboBoxItem>(OnRowReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerCaptureLostEvent.AddClassHandler<ComboBoxItem>((row, _) => ReleaseRow(row));
    }

    // Lets a right press on one list row sound the held button shown in it.
    public static void HookRightPressOnRow(Control row)
    {
        row.AddHandler(InputElement.PointerPressedEvent, (_, e) => OnRowPressed(row, e), RoutingStrategies.Tunnel);
        row.AddHandler(InputElement.PointerReleasedEvent, (_, e) => OnRowReleased(row, e), RoutingStrategies.Tunnel, handledEventsToo: true);
        row.PointerCaptureLost += (_, _) => ReleaseRow(row);
    }

    // Runs the press action of the held button shown in a row on a right press outside the row's text fields, the row holding the pointer until the
    // right button comes up and the press kept from everything else.
    private static void OnRowPressed(Control row, PointerPressedEventArgs e)
    {
        if (!PointerButtons.IsRightPress(e) || e.Source is not Visual source || ListRows.IsInTextField(source) || ShownHeldActionsIn(row) is not { } actions)
        {
            return;
        }

        e.Pointer.Capture(row);
        ReleasesOfHeldRows.AddOrUpdate(row, actions.OnRelease);
        actions.OnPress();
        e.Handled = true;
    }

    // Ends a row's right hold when the right button comes up, keeping the release from everything else.
    private static void OnRowReleased(Control row, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right || !ReleasesOfHeldRows.TryGetValue(row, out _))
        {
            return;
        }

        e.Handled = true;
        ReleaseRow(row);
    }

    // Runs the release action of a row's right hold once, where one is held.
    private static void ReleaseRow(Control row)
    {
        if (!ReleasesOfHeldRows.TryGetValue(row, out var release))
        {
            return;
        }

        ReleasesOfHeldRows.Remove(row);
        release();
    }

    // The actions of the held button shown and enabled in a row, or null where it has none.
    private static HeldActions? ShownHeldActionsIn(Control row)
    {
        foreach (var button in row.GetVisualDescendants().OfType<Button>())
        {
            if (button.IsEffectivelyVisible && button.IsEffectivelyEnabled && ActionsOfButtons.TryGetValue(button, out var actions))
            {
                return actions;
            }
        }

        return null;
    }

    // What a held button runs on its press and on its release.
    private sealed record HeldActions(Action OnPress, Action OnRelease);
}
