using Avalonia;
using Avalonia.Controls;

namespace SpcStudio.Controls;

// A scroll viewer seen as a scrolled view.
public sealed class ScrollViewerView(ScrollViewer scrollViewer) : IScrolledView
{
    // The viewer's own offset, which it holds inside its content itself.
    public Vector Offset
    {
        get
        {
            return scrollViewer.Offset;
        }
        set
        {
            scrollViewer.Offset = value;
        }
    }

    // The viewer's own viewport.
    public Size Viewport
    {
        get
        {
            return scrollViewer.Viewport;
        }
    }

    // The furthest the viewer's bars can take it.
    public Vector FurthestOffset
    {
        get
        {
            return scrollViewer.ScrollBarMaximum;
        }
    }
}
