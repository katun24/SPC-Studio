namespace SpcStudio.Audio;

// Evens out the cycles of a run of a sample's points: the run is taken as the nearest whole number of the sound's cycles, every cycle is replaced by
// their average, and the result is mixed with the original by how strong the effect is to be.
public static class CycleSmoothing
{
    private const int MinCycleCount = 2;
    private const double MirroredBinShare = 2;

    // Whether a run of points holds at least two of a pitched sound's cycles to average.
    public static bool CanSmooth(int pointCount, BrrPitch.Measurement pitch)
    {
        return pitch.IsPitched && CycleCountIn(pointCount, pitch) >= MinCycleCount;
    }

    // The points with their cycles evened out, the effect being the fraction of the way from the original to the average; the run must hold two cycles.
    public static short[] Smoothed(short[] points, BrrPitch.Measurement pitch, double effect)
    {
        var averaged = AveragedCycles(points, CycleCountIn(points.Length, pitch));
        var smoothed = new short[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            smoothed[i] = DspPoint.Rounded(points[i] + effect * (averaged[i] - points[i]));
        }

        return smoothed;
    }

    // How many whole cycles of the sound a run of points is nearest to.
    private static int CycleCountIn(int pointCount, BrrPitch.Measurement pitch)
    {
        return (int)Math.Round(pointCount / pitch.PeriodPoints);
    }

    // The average of the run's cycles repeated across the whole run, which is the run rebuilt from the tones that repeat once per cycle and nothing else.
    private static double[] AveragedCycles(short[] points, int cycleCount)
    {
        var circle = new UnitCircle(points.Length);
        var averaged = new double[points.Length];
        for (int bin = 0; bin <= points.Length / MirroredBinShare; bin += cycleCount)
        {
            AddTone(points, bin, circle, averaged);
        }

        return averaged;
    }

    // Adds to a sum the one tone of the points that repeats a number of times across the run.
    private static void AddTone(short[] points, int bin, UnitCircle circle, double[] sum)
    {
        double cosineSum = 0;
        double sineSum = 0;
        int phase = 0;
        foreach (short point in points)
        {
            cosineSum += point * circle.Cosines[phase];
            sineSum += point * circle.Sines[phase];
            phase = circle.Advanced(phase, bin);
        }

        // The share of the tone each point gets back: a tone that is its own mirror counts once, every other tone once for itself and once for its mirror.
        double weight = (bin == 0 || bin * MirroredBinShare == points.Length ? 1 : MirroredBinShare) / points.Length;
        phase = 0;
        for (int i = 0; i < points.Length; i++)
        {
            sum[i] += weight * (cosineSum * circle.Cosines[phase] + sineSum * circle.Sines[phase]);
            phase = circle.Advanced(phase, bin);
        }
    }

    // The cosine and the sine at each of a number of even steps around a whole turn.
    private sealed class UnitCircle
    {
        public readonly double[] Cosines;
        public readonly double[] Sines;

        // Works out the cosine and the sine at every step of a turn cut into a number of steps.
        public UnitCircle(int stepCount)
        {
            Cosines = new double[stepCount];
            Sines = new double[stepCount];
            for (int step = 0; step < stepCount; step++)
            {
                double angle = Math.Tau * step / stepCount;
                Cosines[step] = Math.Cos(angle);
                Sines[step] = Math.Sin(angle);
            }
        }

        // A step moved on by a number of steps less than a whole turn, wrapped back into the turn.
        public int Advanced(int step, int stepsOn)
        {
            int moved = step + stepsOn;
            return moved >= Cosines.Length ? moved - Cosines.Length : moved;
        }
    }
}
