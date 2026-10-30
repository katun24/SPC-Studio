using Avalonia;
using Avalonia.Layout;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The '+' under a section's rows that adds one: a square icon button at the rows' left edge, a gap below the last of them.
public class AddRowButton : ThemedButton
{
    // Builds the button with its glyph, its place and its look.
    public AddRowButton()
    {
        Classes.Add(ThemeClasses.SquareIcon);
        HorizontalAlignment = HorizontalAlignment.Left;
        Margin = new Thickness(0, Space.Px6, 0, 0);
        Content = new IconShape { Data = AppIcons.Plus };
    }
}
