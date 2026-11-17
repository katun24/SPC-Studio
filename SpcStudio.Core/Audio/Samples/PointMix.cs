namespace SpcStudio.Audio;

// Runs of points sounded together: added point by point from their first, as long as the longest, and the sum turned down only as far as it needs to
// be to fit a point.
public static class PointMix
{
    private const double UnchangedLevel = 1.0;

    // The runs added together, each from its first point and each read only as it is added, the sum scaled down to fit where its loudest point would not.
    public static short[] Summed(IEnumerable<short[]> runs)
    {
        var sums = Array.Empty<int>();
        foreach (var run in runs)
        {
            if (run.Length > sums.Length)
            {
                Array.Resize(ref sums, run.Length);
            }

            for (int i = 0; i < run.Length; i++)
            {
                sums[i] += run[i];
            }
        }

        double peak = DspPoint.PeakOf<int>(sums);
        double multiplier = peak > short.MaxValue ? short.MaxValue / peak : UnchangedLevel;
        var mixed = new short[sums.Length];
        for (int i = 0; i < sums.Length; i++)
        {
            mixed[i] = DspPoint.Rounded(sums[i] * multiplier);
        }

        return mixed;
    }
}
