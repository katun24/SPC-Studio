namespace SpcStudio.Audio;

// Linear fades over a run of points: up from silence at its first point to its own level at its last, or down the other way.
public static class PointFades
{
    public const int MinimumPointCount = 2;

    // The points faded from silence at the first up to their own level at the last.
    public static short[] FadedIn(short[] points)
    {
        return DspPoint.Scaled(points, index => FractionAlong(index, points.Length));
    }

    // The points faded from their own level at the first down to silence at the last.
    public static short[] FadedOut(short[] points)
    {
        return DspPoint.Scaled(points, index => 1.0 - FractionAlong(index, points.Length));
    }

    // How far a point stands along a run, the first point none of the way and the last all of it.
    private static double FractionAlong(int index, int pointCount)
    {
        return (double)index / (pointCount - 1);
    }
}
