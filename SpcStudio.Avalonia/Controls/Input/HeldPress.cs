using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SpcStudio.Controls;

// A button that sounds something for as long as it is held: one action on the press, and another on the release or on the capture being lost. A right
// press on the list row or the drop-down's row the button is shown in sounds it the same way, for as long as the right button is held, and moving onto
// another row of the same list sounds that row instead.
public static class HeldPress
{
    private static readonly ConditionalWeakTable<Button, HeldActions> ActionsOfButtons = [];
    private static readonly ConditionalWeakTable<Control, RightHold> RightHoldsOfPressedRows = [];

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
        InputElement.PointerMovedEvent.AddClassHandler<ComboBoxItem>(OnRowMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<ComboBoxItem>(OnRowReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerCaptureLostEvent.AddClassHandler<ComboBoxItem>((row, _) => ReleaseRow(row));
    }

    // Lets a right press on one list row sound the held button shown in it.
    public static void HookRightPressOnRow(Control row)
    {
        row.AddHandler(InputElement.PointerPressedEvent, (_, e) => OnRowPressed(row, e), RoutingStrategies.Tunnel);
        row.AddHandler(InputElement.PointerMovedEvent, (_, e) => OnRowMoved(row, e), RoutingStrategies.Tunnel, handledEventsToo: true);
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
        RightHoldsOfPressedRows.AddOrUpdate(row, new RightHold(row, actions.OnRelease));
        actions.OnPress();
        e.Handled = true;
    }

    // Hands a row's right hold on to the row of the same list the pointer has moved onto, where a held button is shown in it, releasing the row
    // sounding until then and pressing the new one; over anything else the row sounding goes on.
    private static void OnRowMoved(Control pressedRow, PointerEventArgs e)
    {
        if (!RightHoldsOfPressedRows.TryGetValue(pressedRow, out var hold) || RowOfSameListAt(pressedRow, e) is not { } row || row == hold.SoundingRow)
        {
            return;
        }

        HoverHolds.MoveHold(pressedRow, row);
        if (ShownHeldActionsIn(row) is not { } actions)
        {
            HoverHolds.MoveHold(pressedRow, hold.SoundingRow);
            return;
        }

        RightHoldsOfPressedRows.AddOrUpdate(pressedRow, new RightHold(row, actions.OnRelease));
        hold.OnRelease();
        actions.OnPress();
    }

    // Ends a row's right hold when the right button comes up, keeping the release from everything else.
    private static void OnRowReleased(Control row, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right || !RightHoldsOfPressedRows.TryGetValue(row, out _))
        {
            return;
        }

        e.Handled = true;
        ReleaseRow(row);
    }

    // Runs the release action of a row's right hold once, where one is held.
    private static void ReleaseRow(Control pressedRow)
    {
        if (!RightHoldsOfPressedRows.TryGetValue(pressedRow, out var hold))
        {
            return;
        }

        RightHoldsOfPressedRows.Remove(pressedRow);
        hold.OnRelease();
    }

    // The row under the pointer in the scrolling list a pressed row stands in, or null where the pointer is over no row of that list.
    private static Control? RowOfSameListAt(Control pressedRow, PointerEventArgs e)
    {
        if (pressedRow.FindAncestorOfType<ScrollViewer>() is not { } listViewer)
        {
            return null;
        }

        var pointOverList = e.GetPosition(listViewer);
        if (!new Rect(listViewer.Bounds.Size).Contains(pointOverList))
        {
            return null;
        }

        for (var visual = listViewer.InputHitTest(pointOverList) as Visual; visual is not null && visual != listViewer; visual = visual.GetVisualParent())
        {
            if (visual is ComboBoxItem || visual is Border border && ListRows.IsListRow(border))
            {
                return (Control)visual;
            }
        }

        return null;
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

    // The row a right hold is sounding, the pressed row or one the pointer has moved onto since, and what ends its sound.
    private sealed record RightHold(Control SoundingRow, Action OnRelease);
}
