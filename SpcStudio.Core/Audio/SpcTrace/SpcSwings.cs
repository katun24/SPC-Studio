using System.Collections.Immutable;

namespace SpcStudio.Audio;

// A block's record of one register's values read for its swings: the runs where the value swung evenly to and fro about one value, each cut into
// stretches where it held one width, in the units of that register's scale.
public static class SpcSwings
{
    private const int MinTurns = 4;
    private const int MinValuesPerHalfCycle = 2;
    private const int MinWidthSwings = 3;
    private const int HalfCyclesPerCycle = 2;
    private const long MinHalfCycleFrames = 2 * SpcTrace.PollFrames;
    private const double MaxHalfCycleFrames = DspPoint.SampleRate / 2;
    private const double MaxHalfCycleRatio = 3;
    private const double MaxJoinedCycleMismatch = 0.15;
    private const double MaxJoinedAcrossSlideCycles = 2;
    public const double SidesPerSwing = 2;
    private const double WidthTolerance = 0.2;
    private const double FullWidthFraction = 0.85;
    private const double WidthPercentile = 0.75;
    private const double MedianPercentile = 0.5;
    private const double CyclesPerTurn = 0.5;
    private const double TurnToleranceCycles = 0.125;
    private const long MinTurnToleranceFrames = 2 * SpcTrace.PollFrames;
    private const double MaxReachOverPeak = 1.5;
    private const double ReversalHeardCycles = 0.25;
    private const long MinHeardSweepFrames = 2 * SpcTrace.PollFrames;

    // A block's path as the stretches it swung evenly about one value at one width, in the order it played them.
    public static ImmutableArray<SpcTraceSwing> SwingsOf(IReadOnlyList<SpcTracePoint> path, long end, SpcSwingScale scale)
    {
        var turns = TurnsOf(path, scale);
        var swings = ImmutableArray.CreateBuilder<SpcTraceSwing>();
        int first = 0;
        for (int at = 1; at <= turns.Count; at++)
        {
            bool isHalfCycle = at < turns.Count && IsHalfCycle(turns[at - 1], turns[at], scale);
            if (isHalfCycle && ContinuesRun(turns, first, at, scale))
            {
                continue;
            }

            if (at - first >= MinTurns)
            {
                long earliestStart = swings.Count > 0 ? swings[^1].End : path[0].Frame;
                foreach (var stretch in StretchesOf(turns.GetRange(first, at - first), path, end, earliestStart, scale))
                {
                    AddJoining(swings, stretch, path, scale);
                }
            }

            first = isHalfCycle ? at - 1 : at;
        }

        return swings.ToImmutable();
    }

    // Whether each value of a path lies inside one of the stretches it swung in.
    public static bool[] SweptValues(IReadOnlyList<SpcTracePoint> path, ImmutableArray<SpcTraceSwing> swings)
    {
        var swept = new bool[path.Count];
        for (int at = 0; at < path.Count; at++)
        {
            swept[at] = SwingAt(swings, path[at].Frame) is not null;
        }

        return swept;
    }

    // The first of a block's stretches of one sweep that a frame lies inside, or null where it lies inside none.
    public static SpcTraceSwing? SwingAt(ImmutableArray<SpcTraceSwing> swings, long frame)
    {
        return swings.FirstOrDefault(swing => swing.IsSwingingAt(frame));
    }

    // How a block's path reads against a swing its sample made elsewhere, set off from the block's key-on - swinging as that one would, going against it,
    // or ending before either could show - and the swing it makes where it does not go against it, or null where none could be heard.
    public static (SpcSwingFit Fit, SpcTraceSwing? Swing) ReadAgainst(
        IReadOnlyList<SpcTracePoint> heardPath, long keyOnFrame, long end, SpcSwingTemplate template, SpcSwingScale scale)
    {
        var path = WithoutLastPollValue(heardPath, end);
        long sweepStart = keyOnFrame + template.DelayFrames;
        var turns = TurnsOf(path, scale);
        var fit = FitOf(path, turns, sweepStart, end, template, scale);
        if (fit == SpcSwingFit.DoesNotSwing)
        {
            return (fit, null);
        }

        return (fit, InferredOf(path, turns, sweepStart, end, template, scale));
    }

    // How a path reads against a template whose sweep sets off at a frame: swinging as it would, going against it, or ending before either could show.
    private static SpcSwingFit FitOf(IReadOnlyList<SpcTracePoint> path, List<Turn> turns, long sweepStart, long end, SpcSwingTemplate template, SpcSwingScale scale)
    {
        double toleranceFrames = TurnToleranceFrames(template);
        double readableReachFraction = Math.Min(1, scale.MinWidthTolerance / (template.Peak * (1 + scale.KeyOnSidePeaks)));
        double readableMoveFrame = sweepStart + scale.FirstTurnCycles * template.CycleFrames * readableReachFraction;
        if (!StaysInReach(path, template, scale) || !LeavesKeyOnAsSweepWould(path, end, sweepStart - toleranceFrames, readableMoveFrame + toleranceFrames, scale))
        {
            return SpcSwingFit.DoesNotSwing;
        }

        var matched = new bool[turns.Count];
        for (int turnNumber = 0; ; turnNumber++)
        {
            double predictedFrame = sweepStart + (scale.FirstTurnCycles + turnNumber * CyclesPerTurn) * template.CycleFrames;
            if (predictedFrame - toleranceFrames > end)
            {
                break;
            }

            bool isPeak = (turnNumber % HalfCyclesPerCycle == 0) == scale.IsFirstTurnPeak;
            int found = turns.FindIndex(turn => IsPeak(path, turn) == isPeak && Math.Abs(turn.Frame - predictedFrame) <= toleranceFrames);
            if (found >= 0)
            {
                matched[found] = true;
            }

            bool isReversalHeard = predictedFrame + template.CycleFrames * ReversalHeardCycles <= end;
            if (isReversalHeard && found < 0)
            {
                return SpcSwingFit.DoesNotSwing;
            }
        }

        if (matched.Contains(false))
        {
            return SpcSwingFit.DoesNotSwing;
        }

        return path.Any(point => point.Value != path[0].Value) ? SpcSwingFit.Swings : SpcSwingFit.TooShortToTell;
    }

    // A template's swing as a path would make it from a frame, about the value its key-on stands for, at the width its own turns reached where it has any,
    // or null where the path ended before the sweep could be heard to set off.
    private static SpcTraceSwing? InferredOf(IReadOnlyList<SpcTracePoint> path, List<Turn> turns, long sweepStart, long end, SpcSwingTemplate template, SpcSwingScale scale)
    {
        if (sweepStart + MinHeardSweepFrames >= end)
        {
            return null;
        }

        double center = KeyOnCenter(path, template, scale);
        var reaches = turns.Where(turn => turn.Frame >= sweepStart).Select(turn => Math.Abs(turn.Units - center)).ToArray();
        double peak = reaches.Length > 0 ? Percentile(reaches, WidthPercentile) : template.Peak;
        int centerValue = scale.KeyOnSidePeaks == 0 ? path[0].Value : scale.ValueOf(center);
        return new SpcTraceSwing(sweepStart, end, centerValue, template.CycleFrames, peak, Math.Min(sweepStart + template.FadeFrames, end));
    }

    // The value a template's swing would be about where a path keys on, in units: the key-on value, moved down by as many of the template's widths as the driver's triangle keys on above its middle.
    private static double KeyOnCenter(IReadOnlyList<SpcTracePoint> path, SpcSwingTemplate template, SpcSwingScale scale)
    {
        return scale.UnitsOf(path[0].Value) - scale.KeyOnSidePeaks * template.Peak;
    }

    // A path without the value it took up in the last poll before its end, where it took one up there.
    private static IReadOnlyList<SpcTracePoint> WithoutLastPollValue(IReadOnlyList<SpcTracePoint> path, long end)
    {
        return path.Count > 1 && end - path[^1].Frame <= SpcTrace.PollFrames ? path.Take(path.Count - 1).ToList() : path;
    }

    // Whether every value of a path stays as close to the value its key-on stands for as a swing reaching a template's width could take it.
    private static bool StaysInReach(IReadOnlyList<SpcTracePoint> path, SpcSwingTemplate template, SpcSwingScale scale)
    {
        double center = KeyOnCenter(path, template, scale);
        double reachLimit = template.Peak * MaxReachOverPeak + scale.MinWidthTolerance;
        return path.All(point => Math.Abs(scale.UnitsOf(point.Value) - center) <= reachLimit);
    }

    // Whether a path leaves the value it keyed on with the way the sweep first turns, no earlier than one frame and no later than another, or holds it until it ends where it ends before the later.
    private static bool LeavesKeyOnAsSweepWould(IReadOnlyList<SpcTracePoint> path, long end, double earliestFrame, double latestFrame, SpcSwingScale scale)
    {
        int keyOnValue = path[0].Value;
        foreach (var point in path)
        {
            if (point.Value != keyOnValue)
            {
                return point.Frame >= earliestFrame && point.Frame <= latestFrame && (point.Value > keyOnValue) == scale.IsFirstTurnPeak;
            }
        }

        return end <= latestFrame;
    }

    // How far a turn may be read from where a template's sweep would turn and still be that turn.
    private static double TurnToleranceFrames(SpcSwingTemplate template)
    {
        return Math.Max(template.CycleFrames * TurnToleranceCycles, MinTurnToleranceFrames);
    }

    // Whether a turn is a peak rather than a trough.
    private static bool IsPeak(IReadOnlyList<SpcTracePoint> path, Turn turn)
    {
        return path[turn.Index].Value > path[turn.Index - 1].Value;
    }

    // Every value of a path at which it turned back, with the middle of the time it was read there and where it stood in units, leaving out the two turns
    // of a single value stepped out of a half cycle and back into it.
    private static List<Turn> TurnsOf(IReadOnlyList<SpcTracePoint> path, SpcSwingScale scale)
    {
        return WithoutSteppedOutValues(EveryTurnOf(path, scale), path, scale);
    }

    // Every value of a path at which it turned back, with the middle of the time it was read there and where it stood in units.
    private static List<Turn> EveryTurnOf(IReadOnlyList<SpcTracePoint> path, SpcSwingScale scale)
    {
        var turns = new List<Turn>();
        for (int at = 1; at + 1 < path.Count; at++)
        {
            int arrival = path[at].Value - path[at - 1].Value;
            int departure = path[at + 1].Value - path[at].Value;
            if (arrival == 0 || departure == 0 || Math.Sign(arrival) == Math.Sign(departure))
            {
                continue;
            }

            double heldMiddle = (path[at].Frame + path[at + 1].Frame - SpcTrace.PollFrames) / 2.0;
            turns.Add(new Turn(at, heldMiddle, scale.UnitsOf(path[at].Value)));
        }

        return turns;
    }

    // A path's turns without each pair that is one value stepped out by less than a jump and straight back, where the turns either side of the pair are then half a cycle apart.
    private static List<Turn> WithoutSteppedOutValues(List<Turn> turns, IReadOnlyList<SpcTracePoint> path, SpcSwingScale scale)
    {
        var kept = new List<Turn>();
        for (int at = 0; at < turns.Count; at++)
        {
            bool isSteppedOut = kept.Count > 0 && at + 2 < turns.Count && turns[at + 1].Index == turns[at].Index + 1
                && !scale.AreApart(path[turns[at].Index].Value, path[turns[at + 1].Index].Value) && IsHalfCycle(kept[^1], turns[at + 2], scale);
            if (isSteppedOut)
            {
                at++;
                continue;
            }

            kept.Add(turns[at]);
        }

        return kept;
    }

    // Whether the move from one turn to the next is half of a swing's cycle: no wider than one swings, through more than one value, and taking a time one can.
    private static bool IsHalfCycle(Turn from, Turn to, SpcSwingScale scale)
    {
        double swing = Math.Abs(to.Units - from.Units);
        double frames = to.Frame - from.Frame;
        return swing <= scale.MaxSwing && to.Index - from.Index >= MinValuesPerHalfCycle && frames >= MinHalfCycleFrames && frames <= MaxHalfCycleFrames;
    }

    // Whether a turn carries on the run of turns from another up to it: swinging about as fast as the half cycle in front of it, and about the value that half cycle swung about.
    private static bool ContinuesRun(List<Turn> turns, int first, int at, SpcSwingScale scale)
    {
        if (at - 1 == first)
        {
            return true;
        }

        double halfCycle = turns[at].Frame - turns[at - 1].Frame;
        double previousHalfCycle = turns[at - 1].Frame - turns[at - 2].Frame;
        if (halfCycle > previousHalfCycle * MaxHalfCycleRatio || previousHalfCycle > halfCycle * MaxHalfCycleRatio)
        {
            return false;
        }

        return Math.Abs(Middle(turns[at - 1], turns[at]) - Middle(turns[at - 2], turns[at - 1])) <= scale.MaxCenterDrift;
    }

    // A run of turns as the stretches it held one width for, every one at the run's one speed and about the value the run swung about before that value moved - or where it started, where it held that
    // value for less than a run is read over - each swing's width read about where that value stood as it swung.
    private static List<SpcTraceSwing> StretchesOf(List<Turn> run, IReadOnlyList<SpcTracePoint> path, long end, long earliestStart, SpcSwingScale scale)
    {
        var middles = new double[run.Count - 1];
        for (int index = 1; index < run.Count; index++)
        {
            middles[index - 1] = Middle(run[index - 1], run[index]);
        }

        int heldCount = HeldMiddleCount(middles, scale);
        bool hasMoved = heldCount < middles.Length;
        double center = !hasMoved || heldCount >= MinTurns - 1 ? Percentile(middles[..heldCount], MedianPercentile) : MovingCenterAtTurn(middles, 0);
        var swings = new double[run.Count - 1];
        for (int index = 1; index < run.Count; index++)
        {
            double reach = run[index].Units - (hasMoved ? MovingCenterAtTurn(middles, index) : center);
            double previousReach = run[index - 1].Units - (hasMoved ? MovingCenterAtTurn(middles, index - 1) : center);
            swings[index - 1] = Math.Abs(reach - previousReach);
        }

        double halfCycle = (run[^1].Frame - run[0].Frame) / (run.Count - 1);
        int centerValue = scale.ValueOf(center);
        long start = Math.Max(PhaseStart(run, halfCycle, scale), earliestStart);
        long lastHoldEnd = path[run[^1].Index + 1].Frame;
        long runEnd = end - lastHoldEnd < halfCycle * HalfCyclesPerCycle ? end : lastHoldEnd;
        var widths = WidthsOf(swings, scale);
        var stretches = new List<SpcTraceSwing>();
        for (int index = 0; index < widths.Count; index++)
        {
            var width = widths[index];
            long widthStart = index == 0 ? start : (long)run[width.First].Frame;
            long widthEnd = index + 1 < widths.Count ? (long)run[widths[index + 1].First].Frame : runEnd;
            double peak = Percentile(swings[width.FlatFirst..(width.FlatLast + 1)], WidthPercentile) / SidesPerSwing;
            long fullWidthFrame = index == 0 ? FullWidthFrame(run, width, center, peak, start) : widthStart;
            stretches.Add(new SpcTraceSwing(widthStart, widthEnd, centerValue, halfCycle * HalfCyclesPerCycle, peak, fullWidthFrame));
        }

        return stretches;
    }

    // How many of a run's half cycles, from its first, swung about the value the ones before them swung about.
    private static int HeldMiddleCount(double[] middles, SpcSwingScale scale)
    {
        int heldCount = 1;
        double heldSum = middles[0];
        while (heldCount < middles.Length && Math.Abs(middles[heldCount] - heldSum / heldCount) <= scale.MaxCenterDrift)
        {
            heldSum += middles[heldCount];
            heldCount++;
        }

        return heldCount;
    }

    // The value a run whose value moved swung about at one of its turns: halfway between the half cycles either side, carried on past the first and the last.
    private static double MovingCenterAtTurn(double[] middles, int turnIndex)
    {
        if (turnIndex == 0)
        {
            return middles[0] - (middles[1] - middles[0]) / 2;
        }

        if (turnIndex == middles.Length)
        {
            return middles[^1] + (middles[^1] - middles[^2]) / 2;
        }

        return (middles[turnIndex - 1] + middles[turnIndex]) / 2;
    }

    // Adds a stretch to the ones read so far, joined onto the last where it carries that one on.
    private static void AddJoining(ImmutableArray<SpcTraceSwing>.Builder swings, SpcTraceSwing stretch, IReadOnlyList<SpcTracePoint> path, SpcSwingScale scale)
    {
        if (swings.Count > 0 && IsCarriedOnBy(swings[^1], stretch, path, scale))
        {
            swings[^1] = swings[^1] with { End = stretch.End };
            return;
        }

        swings.Add(stretch);
    }

    // Whether one stretch carries on the one in front of it at about the same speed and the same width: a stray turn having broken one sweep into two runs
    // within a cycle about the same value, or a slide under it having broken it for a cycle or two with no jump on the way.
    private static bool IsCarriedOnBy(SpcTraceSwing previous, SpcTraceSwing next, IReadOnlyList<SpcTracePoint> path, SpcSwingScale scale)
    {
        double cycleMismatch = Math.Abs(next.CycleFrames - previous.CycleFrames) / previous.CycleFrames;
        if (cycleMismatch > MaxJoinedCycleMismatch || !IsSameWidth(next.Peak * SidesPerSwing, previous.Peak * SidesPerSwing, scale))
        {
            return false;
        }

        long gapFrames = next.Start - previous.End;
        double centerDrift = Math.Abs(scale.UnitsOf(next.Center) - scale.UnitsOf(previous.Center));
        if (gapFrames <= previous.CycleFrames && centerDrift <= scale.MaxCenterDrift)
        {
            return true;
        }

        return gapFrames <= previous.CycleFrames * MaxJoinedAcrossSlideCycles
            && !JumpsBetween(path, previous.End - previous.CycleFrames, next.Start + next.CycleFrames, scale);
    }

    // Whether the value jumped in one write anywhere from one frame to another.
    private static bool JumpsBetween(IReadOnlyList<SpcTracePoint> path, double fromFrame, double toFrame, SpcSwingScale scale)
    {
        for (int at = 1; at < path.Count && path[at].Frame <= toFrame; at++)
        {
            if (path[at].Frame >= fromFrame && scale.AreApart(path[at - 1].Value, path[at].Value))
            {
                return true;
            }
        }

        return false;
    }

    // Where a run's sweep set off from the value it swings about, read off its first turns as the driver's own triangle, which first turns where its scale says and turns again every half cycle after.
    private static long PhaseStart(List<Turn> run, double halfCycle, SpcSwingScale scale)
    {
        bool isFirstPeak = run[1].Units < run[0].Units;
        double firstTurnCycles = isFirstPeak == scale.IsFirstTurnPeak ? scale.FirstTurnCycles : scale.FirstTurnCycles + CyclesPerTurn;
        var starts = new double[MinTurns];
        for (int index = 0; index < MinTurns; index++)
        {
            starts[index] = run[index].Frame - (firstTurnCycles + index * CyclesPerTurn) * HalfCyclesPerCycle * halfCycle;
        }

        return (long)Math.Round(Percentile(starts, MedianPercentile));
    }

    // A run's swings parted into the widths it held, each taking the swings in front of it as the way it moved into that width, and the last taking the stragglers behind it.
    private static List<HeldWidth> WidthsOf(double[] swings, SpcSwingScale scale)
    {
        var flats = new List<(int First, int Last)>();
        int groupFirst = 0;
        for (int at = 1; at <= swings.Length; at++)
        {
            if (at < swings.Length && IsSameWidth(swings[at], Percentile(swings[groupFirst..at], MedianPercentile), scale))
            {
                continue;
            }

            if (at - groupFirst >= MinWidthSwings)
            {
                AddFlat(flats, swings, groupFirst, at - 1, scale);
            }

            groupFirst = at;
        }

        if (flats.Count == 0)
        {
            return [new HeldWidth(0, 0, swings.Length - 1)];
        }

        var widths = new List<HeldWidth>();
        for (int index = 0; index < flats.Count; index++)
        {
            int first = index == 0 ? 0 : flats[index - 1].Last + 1;
            widths.Add(new HeldWidth(first, flats[index].First, flats[index].Last));
        }

        return widths;
    }

    // Adds a run of swings of one width to the widths found so far, joined onto the last of them where the two are the same width.
    private static void AddFlat(List<(int First, int Last)> flats, double[] swings, int first, int last, SpcSwingScale scale)
    {
        if (flats.Count > 0)
        {
            var previous = flats[^1];
            double width = Percentile(swings[first..(last + 1)], MedianPercentile);
            if (IsSameWidth(width, Percentile(swings[previous.First..(previous.Last + 1)], MedianPercentile), scale))
            {
                flats[^1] = (previous.First, last);
                return;
            }
        }

        flats.Add((first, last));
    }

    // Where the first stretch of a run reached its full width, read off the turns it widened through as the straight climb from nothing a fade is, or
    // where it set off where it swung its full width from the first.
    private static long FullWidthFrame(List<Turn> run, HeldWidth width, double center, double peak, long start)
    {
        double fullWidthFrame = start;
        double flatStartFrame = run[width.FlatFirst].Frame;
        for (int index = 0; index <= width.FlatFirst; index++)
        {
            double reach = Math.Abs(run[index].Units - center);
            if (reach >= peak * FullWidthFraction)
            {
                break;
            }

            fullWidthFrame = reach <= 0 ? flatStartFrame : Math.Max(fullWidthFrame, start + (run[index].Frame - start) * peak / reach);
        }

        return (long)Math.Round(Math.Min(fullWidthFrame, flatStartFrame));
    }

    // Whether a swing is as wide as a width, within what the poll's own steps put between two readings of one.
    private static bool IsSameWidth(double swing, double width, SpcSwingScale scale)
    {
        return Math.Abs(swing - width) <= Math.Max(width * WidthTolerance, scale.MinWidthTolerance);
    }

    // The value halfway between two turns, in units.
    private static double Middle(Turn from, Turn to)
    {
        return (from.Units + to.Units) / 2;
    }

    // The value a fraction of the way up a set of values in order.
    internal static double Percentile(double[] values, double fraction)
    {
        var sorted = values.Order().ToArray();
        return sorted[Math.Min((int)(sorted.Length * fraction), sorted.Length - 1)];
    }

    // One value of a path at which it turned back: its index, the middle of the time it was held, and where it stood in units.
    private readonly record struct Turn(int Index, double Frame, double Units);

    // One width a run held: its first swing, counting the ones it widened through, and the first and last it swung its full width.
    private readonly record struct HeldWidth(int First, int FlatFirst, int FlatLast);
}
