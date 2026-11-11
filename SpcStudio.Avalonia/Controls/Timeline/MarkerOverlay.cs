using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpcStudio.Controls;

// A layer laid over a drawn surface and the ruler above it that draws the surface's markers whole, spilling over whatever stands beside the view, and never takes a press.
public abstract class MarkerOverlay : Control
{
    // Builds the layer, which lets every press through to whatever is under it.
    protected MarkerOverlay()
    {
        IsHitTestVisible = false;
    }

    // Whether a position across is inside a view starting at a left edge and running a width.
    protected static bool IsInView(double x, double viewLeft, double viewWidth)
    {
        return x >= viewLeft && x <= viewLeft + viewWidth;
    }

    // Draws a marker hanging from a flag: its line down a stretch and its flag at the top of the ruler's band, where there is one.
    protected static void DrawFlagMarker(DrawingContext dc, MarkerLook look, double x, bool doesFlagPointRight, double? rulerTop, double lineTop, double lineBottom)
    {
        DrawMarkerLine(dc, look, x, lineTop, lineBottom);
        DrawInRuler(dc, rulerTop, () => RulerBand.DrawFlag(dc, look.HeadBrush, x, doesFlagPointRight));
    }

    // Draws a marker shaped as the playback marker - the playback marker itself or the audition marker: its line down a stretch and its head in the ruler's band, where there is one.
    protected static void DrawPlayhead(DrawingContext dc, MarkerLook look, double x, double? rulerTop, double lineTop, double lineBottom)
    {
        DrawMarkerLine(dc, look, x, lineTop, lineBottom);
        DrawInRuler(dc, rulerTop, () => RulerBand.DrawPlayheadHead(dc, look.HeadBrush, x));
    }

    // Draws a marker's line down a stretch.
    protected static void DrawMarkerLine(DrawingContext dc, MarkerLook look, double x, double top, double bottom)
    {
        dc.DrawLine(look.LinePen, new Point(x, top), new Point(x, bottom));
    }

    // Where a visual's top left corner stands in the layer, or null where it cannot be placed.
    protected Point? TopLeftOf(Visual visual)
    {
        return visual.TranslatePoint(default, this);
    }

    // Runs a drawing in the ruler's band, from the top of the band, where there is one.
    private static void DrawInRuler(DrawingContext dc, double? rulerTop, Action draw)
    {
        if (rulerTop is not double top)
        {
            return;
        }

        using (dc.PushTransform(Matrix.CreateTranslation(0, top)))
        {
            draw();
        }
    }
}
