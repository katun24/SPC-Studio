namespace SpcStudio.Audio;

// Blends the two sounds that run into a loop's start into each other: the loop's end into the points before the loop, so the jump back carries on as
// the sound did there, and the points just before the loop into the loop's end, so the loop's start is reached from the same sound on every pass.
public static class LoopCrossfade
{
    private const int LeadInPoints = 64;

    // Whether a loop has points before its start to blend its end into.
    public static bool CanCrossfade(int pointsBeforeLoopCount)
    {
        return pointsBeforeLoopCount > 0;
    }

    // The loop's points with their end blended, over the length's fraction of the loop or as many points as stand before the loop if fewer, in a straight
    // line from all of each point's own sound to all of the sound one loop length earlier.
    public static short[] Crossfaded(short[] loopPoints, short[] pointsBeforeLoop, double lengthFraction)
    {
        return EndBlendedInto(loopPoints, pointsBeforeLoop, (int)Math.Min(BlendedCountOf(loopPoints.Length, lengthFraction), pointsBeforeLoop.Length));
    }

    // How many of a loop's points a crossfade over a fraction of its length blends, where as many stand before the loop.
    public static long BlendedCountOf(long loopPointCount, double lengthFraction)
    {
        return (long)Math.Round(loopPointCount * lengthFraction);
    }

    // The points before a loop with their last few blended into the loop's own last points, in a straight line from all of their own sound to all of the
    // loop's, so the sound arriving at the loop's start is the same on the first pass as on every pass after.
    public static short[] LedIn(short[] pointsBeforeLoop, short[] loopPoints)
    {
        return EndBlendedInto(pointsBeforeLoop, loopPoints, Math.Min(LeadInPoints, Math.Min(pointsBeforeLoop.Length, loopPoints.Length)));
    }

    // Points whose last ones, a count of them, are blended in a straight line into another run's last as many, from all of each point's own sound at
    // the first to all of the other's at the last.
    private static short[] EndBlendedInto(short[] points, short[] otherPoints, int blendedCount)
    {
        int firstBlendedPoint = points.Length - blendedCount;
        int firstOtherPoint = otherPoints.Length - blendedCount;
        var blended = (short[])points.Clone();
        for (int step = 0; step < blendedCount; step++)
        {
            int blendedPoint = firstBlendedPoint + step;
            double otherShare = (step + 1) / (double)blendedCount;
            blended[blendedPoint] = DspPoint.Rounded(points[blendedPoint] + otherShare * (otherPoints[firstOtherPoint + step] - points[blendedPoint]));
        }

        return blended;
    }
}
