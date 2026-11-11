using Avalonia.Controls;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The rows of a drop-down's list that cannot be picked: the line between two groups of rows, and a row shown grayed out.
public static class UnpickableRows
{
    // A line between two groups of rows.
    public static ComboBoxItem Divider()
    {
        var divider = Grayed(null);
        divider.Classes.Add(ThemeClasses.Divider);
        return divider;
    }

    // A row showing what it is given, grayed out.
    public static ComboBoxItem Grayed(object? content)
    {
        return new ComboBoxItem { Content = content, IsEnabled = false };
    }

    // Whether a drop-down's item can be picked, which is every item but these rows.
    public static bool IsPickable(object? item)
    {
        return item is not ComboBoxItem { IsEnabled: false };
    }
}
