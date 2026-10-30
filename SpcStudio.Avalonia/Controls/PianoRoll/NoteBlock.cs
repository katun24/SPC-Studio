using Avalonia;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The block a note on the roll is drawn as, and so the block every other thing standing for a stretch of the song is drawn as: a fill, a border in
// the fill's own color darkened, and a gutter on all four sides.
public static class NoteBlock
{
    public const double MinDrawnWidth = 2;
    private const double MinDrawnHeight = 1;
    private const double BorderDarkening = 0.45;
    private const double PendingMarkDarkening = 0.3;
    private const double BorderThickness = 1;
    private const byte PreviewFillAlpha = 0x38;
    private const byte PreviewBorderAlpha = 0x90;
    private const double StripePeriod = 8;
    private const double StripeThickness = 1;

    // The rectangle a block is drawn as, from the span it covers and the row or lane it sits in, inset at the top and bottom by the given amount.
    public static Rect BlockRect(double x, double width, double top, double height, double inset = Space.Px2)
    {
        return new Rect(x + Space.Px2, top + inset, Math.Max(width - Space.Px2 * 2, MinDrawnWidth), Math.Max(height - inset * 2, MinDrawnHeight));
    }

    // The border a block of that fill wears: the fill blended most of the way to black.
    public static Pen BorderPen(Color fill)
    {
        return new Pen(new SolidColorBrush(ChannelPalette.Blend(fill, Palette.Black, BorderDarkening)), BorderThickness);
    }

    // The border a block of that fill brush wears.
    public static Pen BorderPen(IBrush fill)
    {
        return BorderPen(ColorOf(fill));
    }

    // The solid color drawn over a block for what is about to land on it: the fill blended toward black, a little less far than its border.
    public static Brush PendingMarkFill(Color fill)
    {
        return new SolidColorBrush(ChannelPalette.Blend(fill, Palette.Black, PendingMarkDarkening));
    }

    // The pen a block's own cut stripes are drawn with, in the color drawn over it for what is about to land on it.
    public static Pen CutStripePenOf(IBrush pendingMarkFill)
    {
        return new Pen(pendingMarkFill, StripeThickness);
    }

    // The translucent fill a block is previewed in before a click puts it down.
    public static Brush PreviewFill(Color color)
    {
        return new SolidColorBrush(ChannelPalette.WithAlpha(color, PreviewFillAlpha));
    }

    // The border a previewed block wears, the same color a little less translucent.
    public static Pen PreviewBorderPen(Color color)
    {
        return new Pen(new SolidColorBrush(ChannelPalette.WithAlpha(color, PreviewBorderAlpha)), BorderThickness);
    }

    // The strip of a block that a block landing over it would cut away, as the rectangle to clip the stripes to, or null where the two do not overlap.
    public static Rect? CutRect(int start, int end, int cutterStart, int cutterEnd, double height)
    {
        int overlapStart = Math.Max(start, cutterStart);
        int overlapEnd = Math.Min(end, cutterEnd);
        if (overlapEnd <= overlapStart)
        {
            return null;
        }

        return new Rect(overlapStart * RollMetrics.PixelsPerTick, 0, (overlapEnd - overlapStart) * RollMetrics.PixelsPerTick, height);
    }

    // The color a block of that fill brush is drawn in, black for a brush that is not one solid color.
    public static Color ColorOf(IBrush fill)
    {
        return fill is ISolidColorBrush solid ? solid.Color : Palette.AshGray;
    }

    // Draws diagonal stripes over the part of a shape inside a cut in a block's own stripe pen, each one an unbroken line across it.
    public static void DrawCutStripes(DrawingContext dc, Geometry shape, Rect cut, IPen stripePen)
    {
        var area = cut.Intersect(shape.Bounds);
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        using var cutClip = dc.PushClip(area);
        using var shapeClip = dc.PushGeometryClip(shape);
        for (double lineSum = Math.Floor((area.Left + area.Top) / StripePeriod) * StripePeriod; lineSum < area.Right + area.Bottom; lineSum += StripePeriod)
        {
            dc.DrawLine(stripePen, new Point(lineSum - area.Bottom, area.Bottom), new Point(lineSum - area.Top, area.Top));
        }
    }
}
