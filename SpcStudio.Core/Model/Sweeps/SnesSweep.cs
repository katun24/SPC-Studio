namespace SpcStudio.Model;

// What the driver's two sweeps share: a delay byte, and a phase byte advanced by the rate once a tick.
public static class SnesSweep
{
    public const int PhasePeriod = 256;
    public const int MinDelay = 0;
    public const int MaxDelay = 255;
    public const int MinRate = 0;
    public const int MaxRate = 255;
    public const int MinMovingRate = MinRate + 1;
    public const int DefaultDelay = 0;
    public const int DefaultRate = 0;

    public static readonly ValueRange DelayRange = new(MinDelay, MaxDelay);
    public static readonly ValueRange RateRange = new(MinRate, MaxRate);

    // How many ticks one cycle takes at a rate, infinite at a rate of zero, which never moves.
    public static double PeriodTicks(int rate)
    {
        return rate == 0 ? double.PositiveInfinity : (double)PhasePeriod / rate;
    }

    // The rate whose cycle takes nearest a number of ticks, the slowest that moves at all and the fastest the byte holds being as far as it goes.
    public static int RateForPeriodTicks(double periodTicks)
    {
        return Math.Clamp((int)Math.Round(PhasePeriod / periodTicks), MinMovingRate, MaxRate);
    }

    // The setting in a range whose value comes nearest a target, the lowest of any tied.
    public static int NearestSetting(ValueRange range, Func<int, double> valueOf, double target)
    {
        int nearest = range.Min;
        for (int setting = range.Min + 1; setting <= range.Max; setting++)
        {
            if (Math.Abs(valueOf(setting) - target) < Math.Abs(valueOf(nearest) - target))
            {
                nearest = setting;
            }
        }

        return nearest;
    }
}
