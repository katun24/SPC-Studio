using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A list's sunken, bordered frame: a scrolling stack of rows padded in from the frame, the scroll bar's column reserved, and a layer over the whole
// list that takes no pointer; what the rows are is the window's.
public sealed class ListFrame : ThemedBorder
{
    private const string FrameBrushResource = "SunkenBrush";

    // Builds the frame around its scroll viewer and the layer over it.
    public ListFrame()
    {
        Classes.Add(ThemeClasses.SurfaceFrame);
        Background = ThemeResource.Of<IBrush>(FrameBrushResource);
        Scroll = new ScrollViewer
        {
            Focusable = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = RowsPanel,
        };
        Scroll.Classes.Add(ThemeClasses.ReservedBarColumn);
        Child = new Panel { Children = { Scroll, Overlay } };
    }

    // The stack the window fills with the list's rows.
    public StackPanel RowsPanel { get; } = new() { Classes = { ThemeClasses.ListRowsPanel } };

    // The viewer scrolling the rows.
    public ScrollViewer Scroll { get; }

    // A layer over the whole list, under nothing the pointer does, which a window can light.
    public Border Overlay { get; } = new() { IsHitTestVisible = false };
}
