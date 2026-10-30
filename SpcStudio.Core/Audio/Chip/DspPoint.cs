using SpcStudio.Model;

namespace SpcStudio.Audio;

// The DSP's 16-bit audio points: the rate the chip produces them at, the clip every stage of it applies to one, the multiplying of them, and the scaling that
// turns one into the mixer's levels.
public static class DspPoint
{
    public const float FullScale = 32768f;
    public const double SampleRate = 32000.0;
    public const double PointsPerMs = SampleRate / TimeUnits.MsPerSecond;

    // How far a value multiplied by one of the DSP's signed volume registers is shifted back down.
    public const int RegisterProductShift = 7;

    // A value held to what a 16-bit point can hold, as the DSP clips it.
    public static int Clipped(int value)
    {
        return Math.Clamp(value, short.MinValue, short.MaxValue);
    }

    // A value rounded to the nearest point and held to what a 16-bit point can hold.
    public static short Rounded(double value)
    {
        return (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);
    }

    // Points each multiplied by the number given for its place among them, rounded and clamped.
    public static short[] Scaled(short[] points, Func<int, double> multiplierAt)
    {
        var scaled = new short[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            scaled[i] = Rounded(points[i] * multiplierAt(i));
        }

        return scaled;
    }

    // A point as the mixer's level, full scale being one.
    public static float ToLevel(int point)
    {
        return point / FullScale;
    }
}
