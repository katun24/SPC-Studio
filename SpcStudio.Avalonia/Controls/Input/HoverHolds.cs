using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A row kept looking hovered, its buttons in sight, from a press anywhere on it until the element the press captured lets the pointer go: every list
// row and every drop-down's row.
public static class HoverHolds
{
    private static readonly ConditionalWeakTable<InputElement, Control> HeldRowsOfCapturingElements = [];

    // Keeps every drop-down's row in the app, present and future, looking hovered while a press on it is held.
    public static void HoldEveryDropDownRow()
    {
        InputElement.PointerPressedEvent.AddClassHandler<ComboBoxItem>(HoldFromPress, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // Keeps one row looking hovered while a press on it is held.
    public static void KeepHoveredWhilePressed(Control row)
    {
        row.AddHandler(InputElement.PointerPressedEvent, (_, e) => HoldFromPress(row, e), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // Moves the hold of the press an element captured from the row showing it onto another row, which shows it until the element lets the pointer go.
    public static void MoveHold(InputElement captured, Control toRow)
    {
        if (!HeldRowsOfCapturingElements.TryGetValue(captured, out var heldRow))
        {
            return;
        }

        heldRow.Classes.Remove(ThemeClasses.Held);
        toRow.Classes.Add(ThemeClasses.Held);
        HeldRowsOfCapturingElements.AddOrUpdate(captured, toRow);
    }

    // Runs an action with whether a row now looks hovered, by the pointer being over it or by a hold, each time that changes.
    public static void RunOnLooksHoveredChange(Control row, Action<bool> onChanged)
    {
        bool looksHovered = LooksHovered(row);

        // Runs the action where the row's look has changed since it last ran.
        void RunIfChanged()
        {
            if (LooksHovered(row) == looksHovered)
            {
                return;
            }

            looksHovered = !looksHovered;
            onChanged(looksHovered);
        }

        row.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.IsPointerOverProperty)
            {
                RunIfChanged();
            }
        };
        row.Classes.CollectionChanged += (_, _) => RunIfChanged();
    }

    // Marks a row held from a press on it until the element the press captured lets the pointer go.
    private static void HoldFromPress(Control row, PointerPressedEventArgs e)
    {
        if (e.Pointer.Captured is not InputElement captured)
        {
            return;
        }

        row.Classes.Add(ThemeClasses.Held);
        HeldRowsOfCapturingElements.AddOrUpdate(captured, row);

        // Hands the end of the hold, on whichever row shows it by then, on once the captured element lets the pointer go.
        void OnCaptureLost(object? sender, PointerCaptureLostEventArgs args)
        {
            captured.PointerCaptureLost -= OnCaptureLost;
            if (!HeldRowsOfCapturingElements.TryGetValue(captured, out var heldRow))
            {
                return;
            }

            HeldRowsOfCapturingElements.Remove(captured);
            EndHoldOnceHoverReturns(heldRow);
        }

        captured.PointerCaptureLost += OnCaptureLost;
    }

    // Whether a row looks hovered, which it does while the pointer is over it or a hold is on it.
    private static bool LooksHovered(Control row)
    {
        return row.IsPointerOver || row.Classes.Contains(ThemeClasses.Held);
    }

    // Ends a row's hold once its own hover is back, the pointer moves or the window loses the foreground.
    private static void EndHoldOnceHoverReturns(Control row)
    {
        if (row.IsPointerOver || row.FindLogicalAncestorOfType<Window>() is not { } window)
        {
            row.Classes.Remove(ThemeClasses.Held);
            return;
        }

        // Takes the hold off and stops listening for its end.
        void EndHold()
        {
            row.Classes.Remove(ThemeClasses.Held);
            row.PropertyChanged -= OnRowChanged;
            window.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            window.Deactivated -= OnDeactivated;
        }

        // Ends the hold when the row's own hover is back.
        void OnRowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == InputElement.IsPointerOverProperty && row.IsPointerOver)
            {
                EndHold();
            }
        }

        // Ends the hold on any pointer move in the window.
        void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            EndHold();
        }

        // Ends the hold when the window loses the foreground.
        void OnDeactivated(object? sender, EventArgs e)
        {
            EndHold();
        }

        row.PropertyChanged += OnRowChanged;
        window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.Deactivated += OnDeactivated;
    }
}
