using Avalonia;
using Avalonia.Media;

namespace SpcStudio.Controls;

// The shapes drawn as straight edges through points in order: a lane region's band, the ruler's flags and playback marker head, the envelope graph's
// curve and the area under it, and a note's ramps and its sweeps' waves.
public static class StraightEdges
{
    // A filled shape whose edge runs straight through the corners in order and back to the first.
    public static StreamGeometry Filled(params Point[] corners)
    {
        return GeometryOf(context => AddFilled(context, corners));
    }

    // An open line running straight through the points in order.
    public static StreamGeometry Line(IReadOnlyList<Point> points)
    {
        return GeometryOf(context => AddLine(context, points));
    }

    // Adds to a geometry being drawn a filled figure whose edge runs straight through the corners in order and back to the first.
    public static void AddFilled(StreamGeometryContext context, IReadOnlyList<Point> corners)
    {
        context.BeginFigure(corners[0], isFilled: true);
        LineToEachAfterFirst(context, corners);
        context.EndFigure(isClosed: true);
    }

    // Adds to a geometry being drawn an open line running straight through the points in order.
    public static void AddLine(StreamGeometryContext context, IReadOnlyList<Point> points)
    {
        context.BeginFigure(points[0], isFilled: false);
        LineToEachAfterFirst(context, points);
        context.EndFigure(isClosed: false);
    }

    // A geometry holding whatever the drawing adds to it.
    private static StreamGeometry GeometryOf(Action<StreamGeometryContext> draw)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            draw(context);
        }

        return geometry;
    }

    // Runs the figure begun at the first point straight on through every point after it.
    private static void LineToEachAfterFirst(StreamGeometryContext context, IReadOnlyList<Point> points)
    {
        for (int index = 1; index < points.Count; index++)
        {
            context.LineTo(points[index]);
        }
    }
}
