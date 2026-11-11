using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpcStudio.Audio;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// An envelope's rows drawn small as one curve, with no lines and no handles, in a square framed with square corners and a button's height.
public sealed class EnvelopeMiniGraph : ThemedBorder
{
    private readonly CurvePlot _plot = new() { ClipToBounds = true };

    // Builds the framed square around the plot.
    public EnvelopeMiniGraph()
    {
        Classes.Add(ThemeClasses.SurfaceFrame);
        Width = ThemeResource.FieldHeight;
        Height = ThemeResource.FieldHeight;
        Background = DrawnSurface.Background;
        Child = _plot;
    }

    // Points the graph at an envelope's rows, each at the time it takes over.
    public void Bind(IReadOnlyList<EnvelopeChange> rows)
    {
        _plot.Bind(rows);
    }

    // The area inside the frame, which draws the curve.
    private sealed class CurvePlot : Control
    {
        private const double CurveThickness = 1.01;

        private static readonly Pen CurvePen = new(EnvelopeCurve.LineBrush, CurveThickness, lineCap: PenLineCap.Square);

        // The rows run as one curve, or null with no rows.
        private EnvelopeTrace? _trace;

        // Runs the rows as one curve and redraws.
        public void Bind(IReadOnlyList<EnvelopeChange> rows)
        {
            _trace = EnvelopeCurve.TraceOf(rows);
            InvalidateVisual();
        }

        // Draws the curve, where there are rows.
        public override void Render(DrawingContext context)
        {
            if (_trace is { } trace)
            {
                EnvelopeCurve.DrawOnPixelCenters(context, trace, Bounds.Size, CurvePen);
            }
        }
    }
}
