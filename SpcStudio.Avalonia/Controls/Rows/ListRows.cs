using Avalonia;
using Avalonia.Controls;
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
        HoverHolds.KeepHoveredWhilePressed(row);
        HeldPress.HookRightPressOnRow(row);
    }

    // Runs an action on the second press of a left double-click anywhere on a row but its buttons and its text fields.
    public static void OnDoubleClickOutsideControls(Border row, Action action)
    {
        row.PointerPressed += (_, e) =>
        {
            if (e.ClickCount != DoubleClickCount || !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed || e.Source is not Visual source
                || source.FindAncestorOfType<Button>(includeSelf: true) is not null || IsInTextField(source))
            {
                return;
            }

            action();
        };
    }

    // Whether a press on a row landed on a text field in it, where the press is the field's own.
    public static bool IsInTextField(Visual source)
    {
        return source.FindAncestorOfType<TextBox>(includeSelf: true) is not null;
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
}
