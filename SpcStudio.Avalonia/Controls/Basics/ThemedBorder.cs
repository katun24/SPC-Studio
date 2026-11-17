using Avalonia.Controls;

namespace SpcStudio.Controls;

// A border of the app's own that the theme styles as an ordinary border, so every border style reaches it.
public abstract class ThemedBorder : Border
{
    // Themed as an ordinary border.
    protected override Type StyleKeyOverride
    {
        get
        {
            return typeof(Border);
        }
    }
}
