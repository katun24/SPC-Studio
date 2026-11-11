using Avalonia;
using Avalonia.Controls;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A window's content, standing in from the window's edges by the window padding with the same gap between the rows docked in it.
public sealed class WindowBody : DockPanel
{
    // Builds the body with its padding and its gap.
    public WindowBody()
    {
        Margin = new Thickness(Space.Px8);
        VerticalSpacing = Space.Px8;
    }
}
