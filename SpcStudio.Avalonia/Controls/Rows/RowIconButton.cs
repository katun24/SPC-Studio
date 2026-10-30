using Avalonia.Controls;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The button that sits inside a list row: an icon at the row's own height with no frame, its glyph colored by the theme.
public static class RowIconButton
{
    // A row button showing an icon.
    public static Button Build(Geometry icon)
    {
        return new Button { Classes = { ThemeClasses.RowIcon }, Content = new IconShape { Data = icon } };
    }
}
