using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpcStudio.Audio;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// An envelope's traced curve as every envelope graph draws it: silence at the foot of the plot and the top level at its top, the area under the
// curve filled, and the line in the curve's own color.
public static class EnvelopeCurve
{
    private const double PixelSize = 1;

    public static readonly IBrush LineBrush = new ImmutableSolidColorBrush(Palette.SkyBlue);
    private static readonly IBrush CurveFill = new ImmutableSolidColorBrush(Palette.EnvelopeCurveFill);

    // An envelope's rows, each at the time it takes over, run as one curve, or null with no rows.
    public static EnvelopeTrace? TraceOf(IReadOnlyList<EnvelopeChange> rows)
    {
        return rows.Count == 0 ? null : SnesEnvelope.TraceThroughChanges(rows);
    }

    // The bottom edge of a plot of a height, which is silence, never less than a pixel down.
    public static double PlotBottom(double height)
    {
        return Math.Max(EnvelopeTimeAxis.MinPlotSize, height);
    }

    // Where an envelope level lands up a plot of a height.
    public static double YOfLevel(int level, double height)
    {
        return (1 - level / (double)SnesEnvelope.MaxLevel) * PlotBottom(height);
    }

    // Draws the curve across a plot of a size with a pen, and the area under it filled.
    public static void Draw(DrawingContext context, EnvelopeTrace trace, Size plotSize, IPen linePen)
    {
        var curve = CurvePoints(trace, plotSize);
        DrawThrough(context, curve, curve, trace, plotSize, linePen);
    }

    // Draws the curve as the plain drawing does but with the line's points in the middle of the plot's pixel each falls in, so a one-pixel line's flat
    // and upright stretches cover one row or column of pixels alone.
    public static void DrawOnPixelCenters(DrawingContext context, EnvelopeTrace trace, Size plotSize, IPen linePen)
    {
        var curve = CurvePoints(trace, plotSize);
        DrawThrough(context, curve, OnPixelCenters(curve, plotSize), trace, plotSize, linePen);
    }

    // Draws one set of a curve's points as a line with a pen, and the area under another filled to where the trace ends.
    private static void DrawThrough(DrawingContext context, List<Point> fillCurve, List<Point> lineCurve, EnvelopeTrace trace, Size plotSize, IPen linePen)
    {
        double fillEndX = trace.IsSettled ? EnvelopeTimeAxis.PlotRight(plotSize.Width) : EnvelopeTimeAxis.XOfSeconds(trace.LastCorner.Seconds, plotSize.Width);
        context.DrawGeometry(CurveFill, null, StraightEdges.Filled([.. fillCurve, new Point(fillEndX, PlotBottom(plotSize.Height))]));
        context.DrawGeometry(null, linePen, StraightEdges.Line(lineCurve));
    }

    // A curve's points each moved to the middle of the plot's pixel it falls in.
    private static List<Point> OnPixelCenters(List<Point> curve, Size plotSize)
    {
        return [.. curve.Select(point => new Point(PixelCenterInside(point.X, plotSize.Width), PixelCenterInside(point.Y, plotSize.Height)))];
    }

    // The middle of the pixel a position across a plot's extent falls in, the last pixel taking a position on the far edge.
    private static double PixelCenterInside(double position, double extent)
    {
        return DrawnSurface.PixelCenter(Math.Min(position, extent - PixelSize));
    }

    // The curve's points across a plot of a size: silence at key-on, then each of its corners, running on to the right edge where it settles.
    private static List<Point> CurvePoints(EnvelopeTrace trace, Size plotSize)
    {
        List<Point> points =
        [
            new Point(0, PlotBottom(plotSize.Height)),
            .. trace.Corners.Select(corner => new Point(EnvelopeTimeAxis.XOfSeconds(corner.Seconds, plotSize.Width), YOfLevel(corner.Level, plotSize.Height))),
        ];
        if (trace.IsSettled)
        {
            points.Add(new Point(EnvelopeTimeAxis.PlotRight(plotSize.Width), YOfLevel(trace.LastCorner.Level, plotSize.Height)));
        }

        return points;
    }
}
