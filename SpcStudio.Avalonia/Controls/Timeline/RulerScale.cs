using Avalonia.Media;

namespace SpcStudio.Controls;

// A ruler's numbered scale on a rising ladder of label steps: the finest step whose labels stand far enough apart, a labeled line the band's height on each step and plain ticks between.
public sealed class RulerScale
{
    private const int MinorTicksPerStep = 5;

    private readonly IReadOnlyList<double> _labelSteps;
    private readonly double _minLabelSpacing;
    private readonly Func<double, double, string> _labelOf;

    // Takes the steps a labeled line can fall on, rising, the least room between two labeled lines, and what a value is labeled with at a step.
    public RulerScale(IReadOnlyList<double> labelSteps, double minLabelSpacing, Func<double, double, string> labelOf)
    {
        _labelSteps = labelSteps;
        _minLabelSpacing = minLabelSpacing;
        _labelOf = labelOf;
    }

    // Draws the scale between two positions of a band and up to a last value, a value standing at its distance from the value at the band's start times
    // the pixels a unit, its labels only where they start right of a position.
    public void Draw(DrawingContext dc, double pixelsPerUnit, double valueAtStart, double left, double right, double lastValue, double labelsFrom)
    {
        double step = FinestStepSpacedAtLeast(pixelsPerUnit);
        double minorStep = step / MinorTicksPerStep;
        long firstMinor = Math.Max(0, (long)Math.Floor((valueAtStart + left / pixelsPerUnit) / minorStep));
        long lastMinor = (long)Math.Ceiling(Math.Min(lastValue, valueAtStart + right / pixelsPerUnit) / minorStep);
        for (long minor = firstMinor; minor <= lastMinor; minor++)
        {
            double value = minor * minorStep;
            double x = (value - valueAtStart) * pixelsPerUnit;
            if (x < left || x > right || value > lastValue)
            {
                continue;
            }

            if (minor % MinorTicksPerStep != 0)
            {
                RulerBand.DrawMinorTick(dc, x);
                continue;
            }

            RulerBand.DrawLabeledLine(dc, x, RulerBand.LabelLeftEdge(x) >= labelsFrom ? _labelOf(value, step) : null);
        }
    }

    // The finest label step whose labels stand at least the least room apart, or the coarsest where none does.
    private double FinestStepSpacedAtLeast(double pixelsPerUnit)
    {
        foreach (double step in _labelSteps)
        {
            if (step * pixelsPerUnit >= _minLabelSpacing)
            {
                return step;
            }
        }

        return _labelSteps[^1];
    }
}
