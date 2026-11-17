namespace SpcStudio.Controls;

// Where a time from key-on stands across the envelope graph and the ruler above it, logarithmically, edge to edge.
public static class EnvelopeTimeAxis
{
    public const double MinPlotSize = 1;
    private const double MinSeconds = 0.001;
    private const double MaxSeconds = 100;

    private static readonly double DecadesAcrossPlot = Math.Log10(MaxSeconds / MinSeconds);

    // The times a line is drawn at down the graph and numbered on the ruler, with their labels, the first standing on the left edge.
    public static readonly (double Seconds, string Label)[] MarkedTimes = [(0.001, "1ms"), (0.01, "10ms"), (0.1, "100ms"), (1, "1s"), (10, "10s")];

    // The right edge of the plot in a band of a width, never less than the smallest plot size.
    public static double PlotRight(double width)
    {
        return Math.Max(MinPlotSize, width);
    }

    // Where a time lands across a band of a width: anything up to the shortest time on the left edge and forever on the right.
    public static double XOfSeconds(double seconds, double width)
    {
        if (double.IsPositiveInfinity(seconds))
        {
            return PlotRight(width);
        }

        if (seconds <= MinSeconds)
        {
            return 0;
        }

        return Math.Min(1, Math.Log10(seconds / MinSeconds) / DecadesAcrossPlot) * PlotRight(width);
    }
}
