namespace SpcStudio.Audio;

// Points made again at a lower rate through a windowed sinc, whose lowpass just under the new rate's half keeps what the new rate cannot hold from folding back.
public static class PointResampler
{
    private const int KernelHalfWidthInNewPoints = 16;
    private const double CutoffFractionOfNewHalfRate = 0.9;
    private const double BlackmanCenter = 0.42;
    private const double BlackmanFirstCosine = 0.5;
    private const double BlackmanSecondCosine = 0.08;

    // The points made again at a fraction of their own rate below one, at least one point long, or the same empty array where there are none.
    public static short[] Resampled(short[] points, double rateFraction)
    {
        if (points.Length == 0)
        {
            return points;
        }

        double cutoff = rateFraction * CutoffFractionOfNewHalfRate;
        double kernelHalfWidth = KernelHalfWidthInNewPoints / rateFraction;
        var resampled = new short[Math.Max(1, (int)Math.Round(points.Length * rateFraction))];
        for (int i = 0; i < resampled.Length; i++)
        {
            resampled[i] = DspPoint.Rounded(FilteredPointAt(points, i / rateFraction, cutoff, kernelHalfWidth));
        }

        return resampled;
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
