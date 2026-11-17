using Avalonia.Controls;

namespace SpcStudio.Controls;

// A grid of the app's own that the theme styles as an ordinary grid, so the theme's grid rules place its parts.
public abstract class ThemedGrid : Grid
{
    // Themed as an ordinary grid.
    protected override Type StyleKeyOverride
    {
        get
        {
            return typeof(Grid);
        }
    }
}
