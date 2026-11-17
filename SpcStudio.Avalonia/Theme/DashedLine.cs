using Avalonia.Media;

namespace SpcStudio.Theme;

// The one dashed line the app draws, as a pen of any brush and thickness whose dashes and gaps stay the same length whatever its thickness.
public static class DashedLine
{
    private const double DashLength = 4;

    // A dashed pen in a brush at a thickness.
    public static Pen PenOf(IBrush brush, double thickness)
    {
        return new Pen(brush, thickness, new DashStyle([DashLength / thickness, Space.Px3 / thickness], 0));
    }
}
