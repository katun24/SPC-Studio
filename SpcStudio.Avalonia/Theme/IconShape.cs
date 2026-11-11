using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;

namespace SpcStudio.Theme;

// One of the app's icons drawn as a shape: the glyph at the icon size, unstretched.
public sealed class IconShape : ShapePath
{
    // Sizes the shape to the icon size and leaves the glyph's own fitting alone.
    public IconShape()
    {
        Stretch = Stretch.None;
        Width = AppIcons.Size;
        Height = AppIcons.Size;
    }

    // Themed as an ordinary path.
    protected override Type StyleKeyOverride
    {
        get
        {
            return typeof(ShapePath);
        }
    }
}
