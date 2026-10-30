using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Audio;

// A block's VOL pairs read for its tremolos: the two sides summed and taken back from the driver's squaring so its triangle is a triangle again, the swings
// in hundredths of the loudest the pair can be, falling from the top first, and the level a tremolo ducks from.
public static class SpcLevelSwings
{
    private const int SidesPerPair = 2;
    private const int FullSum = SidesPerPair * sbyte.MaxValue;
    private const double FullUnits = 100;
    private const double MaxSwingUnits = FullUnits;
    private const double MaxCenterDriftUnits = 6;
    private const double MinWidthToleranceUnits = 1.5;
    private const double ApartUnits = 10;
    private const double FirstTurnCycles = 0.5;
    private const int KeyOnSidePeaks = 1;
    private const int MinSwingSteps = 3;
    private const int MinBalanceReadSum = 16;
    private const double MaxBalanceDrift = 0.1;
    private const double LowBalancePercentile = 0.1;
    private const double HighBalancePercentile = 0.9;

    // The tremolo's scale: hundredths of the loudest pair, taken back from the squaring.
    public static readonly SpcSwingScale Scale = new(
        UnitsOf, ValueOf, AreApart, MaxSwingUnits, MaxCenterDriftUnits, MinWidthToleranceUnits, FirstTurnCycles, IsFirstTurnPeak: false, KeyOnSidePeaks);

    // The tremolo as a block is read for it: off the level path.
    public static readonly SpcSwingKind TremoloKind = new(Scale, block => PathOf(block.Levels), block => block.Tremolos, (block, tremolo) => block with { Tremolos = [tremolo] });

    // A block's level path as the stretches it swung evenly about one level at one width, in the order it played them, leaving out any that swung through
    // too few of the registers' steps to be told from their rounding, and any where the balance between the two sides moved.
    public static ImmutableArray<SpcTraceSwing> TremolosOf(IReadOnlyList<SpcTraceLevel> levels, long end)
    {
        var path = PathOf(levels);
        return [.. SpcSwings.SwingsOf(path, end, Scale).Where(tremolo => SwingsThroughSteps(path, tremolo) && HoldsBalance(levels, tremolo))];
    }

    // How deep a tremolo ducks the level it swings down from, as a fraction of that level.
    public static double DepthOf(SpcTraceSwing tremolo)
    {
        double topLevelUnits = UnitsOf(tremolo.Center) + tremolo.Peak;
        return topLevelUnits <= 0 ? 0 : Math.Min(SpcSwings.SidesPerSwing * tremolo.Peak / topLevelUnits, 1);
    }

    // A block's levels with each one inside a tremolo as the loudest the voice was across that tremolo, which is the level the tremolo ducks from.
    public static List<SpcTraceLevel> TopLevels(SpcTraceBlock block)
    {
        var tops = new List<SpcTraceLevel>(block.Levels.Length);
        foreach (var level in block.Levels)
        {
            var tremolo = SpcSwings.SwingAt(block.Tremolos, level.Frame);
            tops.Add(tremolo is null ? level : level with { Volume = LoudestBetween(block.Levels, tremolo.Start, tremolo.End) });
        }

        return tops;
    }

    // A level path as the path a swing is read off: the sizes of the two sides summed, a point wherever the sum moved.
    private static List<SpcTracePoint> PathOf(IReadOnlyList<SpcTraceLevel> levels)
    {
        var path = new List<SpcTracePoint>(levels.Count);
        foreach (var level in levels)
        {
            SpcTrace.AddWhereMoved(path, new SpcTracePoint(level.Frame, SumOf(level.Volume)), point => point.Value);
        }

        return path;
    }

    // Whether a swing's sums reached at least the fewest steps apart a tremolo is told from rounding by.
    private static bool SwingsThroughSteps(List<SpcTracePoint> path, SpcTraceSwing swing)
    {
        var sumsDuringSwing = path.Where(point => swing.IsSwingingAt(point.Frame)).Select(point => point.Value).ToList();
        return sumsDuringSwing.Count > 0 && sumsDuringSwing.Max() - sumsDuringSwing.Min() >= MinSwingSteps;
    }

    // Whether the left side's share of the level held within the drift across most of a swing, wherever the level was loud enough for the share to be
    // read, a swing never loud enough for it to be read anywhere not holding it.
    private static bool HoldsBalance(IReadOnlyList<SpcTraceLevel> levels, SpcTraceSwing swing)
    {
        double[] readableLeftShares =
        [
            .. levels
                .Where(level => swing.IsSwingingAt(level.Frame) && SumOf(level.Volume) >= MinBalanceReadSum)
                .Select(level => (double)Math.Abs(level.Volume.Left) / SumOf(level.Volume)),
        ];
        return readableLeftShares.Length > 0 && SpcSwings.Percentile(readableLeftShares, HighBalancePercentile) - SpcSwings.Percentile(readableLeftShares, LowBalancePercentile) <= MaxBalanceDrift;
    }

    // The loudest pair a level path held anywhere from one frame up to another, the one in force at the first counted.
    private static VoiceVolume LoudestBetween(IReadOnlyList<SpcTraceLevel> levels, long fromFrame, long toFrame)
    {
        var loudest = levels[0].Volume;
        int loudestSum = -1;
        for (int at = 0; at < levels.Count && levels[at].Frame < toFrame; at++)
        {
            bool isInForceAfterFromFrame = at + 1 >= levels.Count || levels[at + 1].Frame > fromFrame;
            int sum = SumOf(levels[at].Volume);
            if (isInForceAfterFromFrame && sum > loudestSum)
            {
                loudest = levels[at].Volume;
                loudestSum = sum;
            }
        }

        return loudest;
    }

    // The sizes of a pair's two sides added together, the sign of each being its phase.
    private static int SumOf(VoiceVolume volume)
    {
        return Math.Abs(volume.Left) + Math.Abs(volume.Right);
    }

    // A sum of the two sides in hundredths of the loudest pair, taken back from the driver's squaring.
    private static double UnitsOf(int sum)
    {
        return FullUnits * Math.Sqrt((double)sum / FullSum);
    }

    // The sum of the two sides a number of hundredths stands for.
    private static int ValueOf(double units)
    {
        double shareBeforeSquaring = Math.Max(units, 0) / FullUnits;
        return (int)Math.Round(shareBeforeSquaring * shareBeforeSquaring * FullSum);
    }

    // Whether two sums are further apart than one write of a swing moves.
    private static bool AreApart(int sum, int other)
    {
        return Math.Abs(UnitsOf(sum) - UnitsOf(other)) > ApartUnits;
    }
}
