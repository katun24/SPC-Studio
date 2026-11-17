namespace SpcStudio.Audio;

// Points made again at another rate through a windowed sinc, whose lowpass just under the lower of the two rates' halves keeps what the lower rate
// cannot hold from folding back.
public static class PointResampler
{
    private const int KernelHalfWidthInLowerRatePoints = 16;
    private const double CutoffFractionOfLowerHalfRate = 0.9;
    private const double SameRateFraction = 1.0;
    private const double BlackmanCenter = 0.42;
    private const double BlackmanFirstCosine = 0.5;
    private const double BlackmanSecondCosine = 0.08;

    // The points made again at a fraction of their own rate, at least one point long, or the same empty array where there are none.
    public static short[] Resampled(short[] points, double rateFraction)
    {
        if (points.Length == 0)
        {
            return points;
        }

        return ResampledFrom(points, rateFraction, 0, ResampledCount(points.Length, rateFraction));
    }

    // How many points a run of points made again at a fraction of its own rate comes to, at least one.
    public static int ResampledCount(int pointCount, double rateFraction)
    {
        return Math.Max(1, (int)Math.Round(pointCount * rateFraction));
    }

    // A count of points made again at a fraction of their own rate, the first standing at a position between the points and each next one a step of the
    // new rate further on.
    public static short[] ResampledFrom(short[] points, double rateFraction, double firstPosition, int count)
    {
        double lowerRateFraction = LowerRateFraction(rateFraction);
        double cutoff = lowerRateFraction * CutoffFractionOfLowerHalfRate;
        double kernelHalfWidth = KernelHalfWidthInLowerRatePoints / lowerRateFraction;
        var resampled = new short[count];
        for (int i = 0; i < count; i++)
        {
            resampled[i] = DspPoint.Rounded(FilteredPointAt(points, firstPosition + i / rateFraction, cutoff, kernelHalfWidth));
        }

        return resampled;
    }

    // How many of the old points either side of a position a point made at a fraction of their rate reads.
    public static int ReachInOldPoints(double rateFraction)
    {
        return (int)Math.Ceiling(KernelHalfWidthInLowerRatePoints / LowerRateFraction(rateFraction));
    }

    // The lower of the two rates as a fraction of the old one: the new rate where it is lower, and the old rate itself where the new one is higher.
    private static double LowerRateFraction(double rateFraction)
    {
        return Math.Min(rateFraction, SameRateFraction);
    }

    // The points' value at a position between them, lowpassed at a cutoff given as a fraction of their own half rate, the kernel reaching a half width either side.
    private static double FilteredPointAt(short[] points, double position, double cutoff, double kernelHalfWidth)
    {
        int first = Math.Max(0, (int)Math.Ceiling(position - kernelHalfWidth));
        int last = Math.Min(points.Length - 1, (int)Math.Floor(position + kernelHalfWidth));
        double sum = 0.0;
        for (int point = first; point <= last; point++)
        {
            double distance = point - position;
            sum += points[point] * cutoff * NormalizedSinc(cutoff * distance) * BlackmanWindow(distance / kernelHalfWidth);
        }

        return sum;
    }

    // The sinc whose zeros fall on the whole numbers other than zero.
    private static double NormalizedSinc(double x)
    {
        if (x == 0.0)
        {
            return 1.0;
        }

        double angle = Math.PI * x;
        return Math.Sin(angle) / angle;
    }

    // The Blackman window at a distance from its center given as a fraction of its half width, nothing past either end.
    private static double BlackmanWindow(double fractionOfHalfWidth)
    {
        if (Math.Abs(fractionOfHalfWidth) >= 1.0)
        {
            return 0.0;
        }

        double angle = Math.PI * fractionOfHalfWidth;
        return BlackmanCenter + BlackmanFirstCosine * Math.Cos(angle) + BlackmanSecondCosine * Math.Cos(2.0 * angle);
    }
}
