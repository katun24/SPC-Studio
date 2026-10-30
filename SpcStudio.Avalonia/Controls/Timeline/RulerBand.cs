using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// What every band counting time or pitch along a drawn surface draws alike: its height, its background and baseline, its ticks, its labels, its flags and the playback marker's head.
public static class RulerBand
{
    public const double Height = 22;
    public const double FlagSize = 9;
    private const double FlagGrabSlack = 5;
    private const double MinorTickHeight = 6;
    private const double LabelFontSize = 10;
    private const double PlayheadHeadHalfWidth = 5;
    private const double PlayheadHeadTopAboveBaseline = 12;
    private const double PlayheadHeadTipAboveBaseline = 2;

    public static readonly IBrush LabelBrush = new ImmutableSolidColorBrush(Palette.SilverGray);

    // Fills a band of a width and draws the line along its foot.
    public static void DrawBackground(DrawingContext dc, double width)
    {
        dc.DrawRectangle(DrawnSurface.Background, null, new Rect(0, 0, width, Height));
        dc.DrawLine(DrawnSurface.BarLinePen, new Point(0, Height), new Point(width, Height));
    }

    // Draws a short plain tick standing on the band's foot.
    public static void DrawMinorTick(DrawingContext dc, double x)
    {
        dc.DrawLine(DrawnSurface.GridLinePen, new Point(x, Height - MinorTickHeight), new Point(x, Height));
    }

    // Draws a line the whole height of the band but on its left edge, with its label just right of it where it has one.
    public static void DrawLabeledLine(DrawingContext dc, double x, string? label)
    {
        if (x > 0)
        {
            dc.DrawLine(DrawnSurface.BarLinePen, new Point(x, 0), new Point(x, Height));
        }

        if (label is not null)
        {
            DrawLabelRightOf(dc, LabelText(label), x);
        }
    }

    // A label's text as every band draws it.
    public static FormattedText LabelText(string label)
    {
        return AppFonts.UiText(label, LabelFontSize, LabelBrush);
    }

    // Draws a label just right of the line it numbers.
    public static void DrawLabelRightOf(DrawingContext dc, FormattedText label, double x)
    {
        dc.DrawText(label, new Point(LabelLeftEdge(x), Space.Px2));
    }

    // Where a label right of a line starts.
    public static double LabelLeftEdge(double x)
    {
        return x + Space.Px3;
    }

    // Where a label right of a line ends.
    public static double LabelRightEdge(FormattedText label, double x)
    {
        return LabelLeftEdge(x) + label.Width;
    }

    // Draws a marker's flag: a right triangle hanging from the top of the band against the marker's line, its tip on one side of it.
    public static void DrawFlag(DrawingContext dc, IBrush brush, double x, bool doesPointRight)
    {
        double tipX = doesPointRight ? x + FlagSize : x - FlagSize;
        dc.DrawGeometry(brush, null, StraightEdges.Filled(new Point(x, 0), new Point(tipX, 0), new Point(x, FlagSize)));
    }

    // Whether a press at a position takes hold of a flag drawn from a line, on the side the flag points to.
    public static bool IsFlagGrabbed(double x, double lineX, bool doesPointRight)
    {
        double distance = x - lineX;
        return doesPointRight ? distance >= -FlagGrabSlack && distance <= FlagSize : distance >= -FlagSize && distance <= FlagGrabSlack;
    }

    // Draws the downward triangle the playback marker's line hangs from.
    public static void DrawPlayheadHead(DrawingContext dc, IBrush brush, double x)
    {
        var head = StraightEdges.Filled(
            new Point(x - PlayheadHeadHalfWidth, Height - PlayheadHeadTopAboveBaseline),
            new Point(x + PlayheadHeadHalfWidth, Height - PlayheadHeadTopAboveBaseline),
            new Point(x, Height - PlayheadHeadTipAboveBaseline));
        dc.DrawGeometry(brush, null, head);
    }
}
