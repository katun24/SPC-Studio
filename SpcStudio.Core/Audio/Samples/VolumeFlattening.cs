namespace SpcStudio.Audio;

// Takes one steady fade out of a run of points: the level is measured near its start and near its end, and the run is turned up or down gradually along
// itself so that its end comes out as loud as its start, its first point left as it is.
public static class VolumeFlattening
{
    private const int MeasuredStretchesPerRun = 2;

    // The points with their steady fade taken out, or the same points where either end is silent and has no level to match.
    public static short[] Flattened(short[] points, BrrPitch.Measurement pitch)
    {
        int stretchPoints = MeasuredStretchPoints(points.Length, pitch);
        var energySums = RunningSums.OfEnergy(points, 0, points.Length);
        double startEnergy = energySums[stretchPoints];
        double endEnergy = energySums[points.Length] - energySums[points.Length - stretchPoints];
        if (startEnergy == 0 || endEnergy == 0)
        {
            return (short[])points.Clone();
        }

        // How fast the level is turned up per point, the distance between the two stretches' middles taking it from the end's level to the start's.
        double growthPerPoint = Math.Log(Math.Sqrt(startEnergy / endEnergy)) / (points.Length - stretchPoints);
        return DspPoint.Scaled(points, index => Math.Exp(growthPerPoint * index));
    }

    // How many points each end's level is measured over: the most whole periods of the sound that fit in half the run, or half the run where the sound
    // has no pitch or no whole period fits.
    private static int MeasuredStretchPoints(int pointCount, BrrPitch.Measurement pitch)
    {
        int halfRunPoints = pointCount / MeasuredStretchesPerRun;
        if (!pitch.IsPitched || pitch.PeriodPoints > halfRunPoints)
        {
            return halfRunPoints;
        }

        return (int)Math.Round(Math.Floor(halfRunPoints / pitch.PeriodPoints) * pitch.PeriodPoints);
    }
}
