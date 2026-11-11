using SpcStudio.Audio;

namespace SpcStudio.Import;

// The speed each instrument's swings of one sweep go at, read once from every one of them together, and a swing read close to its instrument's speed taking it.
public static class SpcSwingSpeeds
{
    public const double MaxCycleMismatch = 0.1;
    private const double MedianWeightFraction = 0.5;

    // The cycle each instrument's swings take, as the middle of every one read, each counting for as long as it swung, keyed by the instrument's id.
    public static Dictionary<int, double> CycleFramesByInstrument(IEnumerable<(int InstrumentId, SpcTraceSwing Swing)> swings)
    {
        return swings
            .GroupBy(instrumentSwing => instrumentSwing.InstrumentId)
            .ToDictionary(group => group.Key, group => WeightedMedianCycleFrames([.. group.Select(instrumentSwing => instrumentSwing.Swing)]));
    }

    // The swing at its instrument's cycle where the instrument has one and it was read within the mismatch of it, and as it was read otherwise.
    public static SpcTraceSwing Steadied(SpcTraceSwing swing, IReadOnlyDictionary<int, double> cycleFramesByInstrument, int instrumentId)
    {
        if (!cycleFramesByInstrument.TryGetValue(instrumentId, out double instrumentCycleFrames))
        {
            return swing;
        }

        bool isCloseToInstrumentCycle = Math.Abs(swing.CycleFrames - instrumentCycleFrames) <= instrumentCycleFrames * MaxCycleMismatch;
        return isCloseToInstrumentCycle ? swing with { CycleFrames = instrumentCycleFrames } : swing;
    }

    // The cycle half of the swings' swinging time was read at or under, the shortest cycle first.
    private static double WeightedMedianCycleFrames(List<SpcTraceSwing> swings)
    {
        var swingsShortestCycleFirst = swings.OrderBy(swing => swing.CycleFrames).ToList();
        double totalSwingFrames = swingsShortestCycleFirst.Sum(SwingFrames);
        double framesSoFar = 0;
        foreach (var swing in swingsShortestCycleFirst)
        {
            framesSoFar += SwingFrames(swing);
            if (framesSoFar >= totalSwingFrames * MedianWeightFraction)
            {
                return swing.CycleFrames;
            }
        }

        return swingsShortestCycleFirst[^1].CycleFrames;
    }

    // How long a swing swung for, at least a frame.
    private static double SwingFrames(SpcTraceSwing swing)
    {
        return Math.Max(swing.End - swing.Start, 1);
    }
}
