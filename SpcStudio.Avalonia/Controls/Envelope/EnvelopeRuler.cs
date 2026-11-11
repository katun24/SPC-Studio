using Avalonia.Controls;
using Avalonia.Media;

namespace SpcStudio.Controls;

// The band above the envelope graph: each marked time numbered over the line the graph draws down at it.
public sealed class EnvelopeRuler : Control
{
    // Draws the background and a numbered line at each marked time.
    public override void Render(DrawingContext dc)
    {
        double width = Bounds.Width;
        RulerBand.DrawBackground(dc, width);
        foreach (var (seconds, label) in EnvelopeTimeAxis.MarkedTimes)
        {
            RulerBand.DrawLabeledLine(dc, EnvelopeTimeAxis.XOfSeconds(seconds, width), label);
        }
    }
}
