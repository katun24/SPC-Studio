using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A row of a list a window shows a row at a time: as tall as a row button, colored while the pointer is over it and while it is selected, its buttons
// shown only then, and kept looking hovered from a press on it to the release.
public static class ListRows
{
    public const double RowHeight = 22;
    private const int DoubleClickCount = 2;

    // A list row holding a control.
    public static Border Build(Control content)
    {
        var row = new Border { MinHeight = RowHeight, Child = content };
        MakeListRow(row);
        return row;
    }

    // Gives a border of any height a list row's look: colored while the pointer is over it, its buttons shown only then, and kept looking hovered from a press on it to the release.
    public static void MakeListRow(Border row)
    {
        row.Classes.Add(ThemeClasses.ListRow);
        KeepHoveredWhilePressed(row);
    }

    // Runs an action on the second press of a left double-click anywhere on a row but its buttons and its text fields.
    public static void OnDoubleClickOutsideControls(Border row, Action action)
    {
        row.PointerPressed += (_, e) =>
        {
            if (e.ClickCount != DoubleClickCount || !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed || e.Source is not Visual source
                || source.FindAncestorOfType<Button>(includeSelf: true) is not null || source.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
            {
                return;
            }

            action();
        };
    }

    // Marks a row as selected, or takes the mark off.
    public static void ShowSelected(Border row, bool isSelected)
    {
        row.Classes.Set(ThemeClasses.Selected, isSelected);
    }

    // Whether a row is marked as selected.
    public static bool IsShownSelected(Border row)
    {
        return row.Classes.Contains(ThemeClasses.Selected);
    }

    // Keeps a row looking hovered, its buttons in sight, from a press anywhere on it until the element the press captured lets the pointer go.
    private static void KeepHoveredWhilePressed(Border row)
    {
        row.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.Pointer.Captured is not InputElement captured)
            {
                return;
            }

            row.Classes.Add(ThemeClasses.Held);
            void OnCaptureLost(object? sender, PointerCaptureLostEventArgs args)
            {
                captured.PointerCaptureLost -= OnCaptureLost;
                EndHoldOnceHoverReturns(row);
            }

            captured.PointerCaptureLost += OnCaptureLost;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // Ends a row's hold once its own hover is back, the pointer moves or the window loses the foreground.
    private static void EndHoldOnceHoverReturns(Border row)
    {
        if (row.IsPointerOver || TopLevel.GetTopLevel(row) is not Window window)
        {
            row.Classes.Remove(ThemeClasses.Held);
            return;
        }

        void EndHold()
        {
            row.Classes.Remove(ThemeClasses.Held);
            row.PropertyChanged -= OnRowChanged;
            window.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            window.Deactivated -= OnDeactivated;
        }

        void OnRowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == InputElement.IsPointerOverProperty && row.IsPointerOver)
            {
                EndHold();
            }
        }

        void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            EndHold();
        }

        void OnDeactivated(object? sender, EventArgs e)
        {
            EndHold();
        }

        row.PropertyChanged += OnRowChanged;
        window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.Deactivated += OnDeactivated;
    }
}
