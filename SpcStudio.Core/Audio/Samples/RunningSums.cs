namespace SpcStudio.Audio;

// Running sums over a run of values, each entry holding the sum of every value before it, so the sum over any stretch of them is two lookups.
public static class RunningSums
{
    // The running sums of a value over a count of offsets.
    public static double[] Of(int offsetCount, Func<int, double> valueAt)
    {
        var sumBeforeOffset = new double[offsetCount + 1];
        for (int offset = 0; offset < offsetCount; offset++)
        {
            sumBeforeOffset[offset + 1] = sumBeforeOffset[offset] + valueAt(offset);
        }

        return sumBeforeOffset;
    }

    // The running sums of the points' energy, each point squared, from a first point up to the boundary past a last.
    public static double[] OfEnergy(short[] points, int firstPoint, int endPoint)
    {
        return Of(endPoint - firstPoint, offset => (double)points[firstPoint + offset] * points[firstPoint + offset]);
    }
}
