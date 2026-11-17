namespace SpcStudio.Audio;

// Where a peak in evenly spaced scores really stands between two of them, by fitting a parabola to the peak's score and its two neighbors'.
public static class ParabolicPeak
{
    private const double FlatPeakCurvature = 1e-12;
    private const double NoOffset = 0;
    private const double FurthestOffset = 1;

    // How far from the middle score the parabola through three scores peaks, in steps between scores, or no offset where the three are flat or the
    // parabola peaks further than one step away.
    public static double OffsetOf(double before, double at, double after)
    {
        double curvature = before - 2 * at + after;
        if (Math.Abs(curvature) < FlatPeakCurvature)
        {
            return NoOffset;
        }

        double offset = 0.5 * (before - after) / curvature;
        return Math.Abs(offset) <= FurthestOffset ? offset : NoOffset;
    }
}
