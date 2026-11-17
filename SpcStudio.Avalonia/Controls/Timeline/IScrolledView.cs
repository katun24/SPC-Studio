using Avalonia;

namespace SpcStudio.Controls;

// A view onto content larger than itself, showing it from an offset into that content.
public interface IScrolledView
{
    // Where the view starts in the content, held to what the content allows as it is set.
    Vector Offset { get; set; }

    // How much of the content the view shows at once.
    Size Viewport { get; }

    // The furthest the view can start in the content on each axis.
    Vector FurthestOffset { get; }
}
