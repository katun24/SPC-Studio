using System.Collections.Immutable;

namespace SpcStudio.Audio;

// A block's swings read for the value they swung about moving: each slide the pitch made while a vibrato kept turning on top of it, as a walk from the
// value the swing was about before it to the one it was about after.
public static class SpcPitchDrifts
{
    private const double HoldToleranceSemitones = 0.15;
    private const double MinDriftSemitones = 0.5;
    private const double MoveQuarter = 0.25;
    private const double MoveThreeQuarters = 0.75;

    // Every slide a block's swings made, in the order they made them.
    public static ImmutableArray<SpcTraceWalk> DriftsOf(SpcTraceBlock block)
    {
        var drifts = ImmutableArray.CreateBuilder<SpcTraceWalk>();
        if (block.Vibratos.IsDefaultOrEmpty || block.Path.IsDefaultOrEmpty)
        {
            return drifts.ToImmutable();
        }

        var heard = new HeardSemitones(block.Path, block.End);
        foreach (var span in SpansOf(block.Vibratos))
        {
            AddDrifts(drifts, block.Path, CentersAcross(heard, span, ReachOf(block, span)), span.CycleFrames);
        }

        return drifts.ToImmutable();
    }

    // A block's swings as the spans they swung without a break, the stretches of one sweep that changed width taken together at the first one's speed.
    private static List<SwingSpan> SpansOf(ImmutableArray<SpcTraceSwing> vibratos)
    {
        var spans = new List<SwingSpan>();
        foreach (var vibrato in vibratos)
        {
            if (spans.Count > 0 && vibrato.Start <= spans[^1].End)
            {
                spans[^1] = spans[^1] with { End = Math.Max(spans[^1].End, vibrato.End) };
                continue;
            }

            spans.Add(new SwingSpan(vibrato.Start, vibrato.End, vibrato.CycleFrames));
        }

        return spans;
    }

    // The frames a span's averaging may reach to: no further than the notes it swung in, and than the nearest write either side of it that jumped to another note.
    private static (long First, long Last) ReachOf(SpcTraceBlock block, SwingSpan span)
    {
        var path = block.Path;
        long first = path[0].Frame;
        long last = block.End;
        foreach (var note in block.Notes)
        {
            if (note.Start <= span.Start && span.Start < note.End)
            {
                first = Math.Max(first, note.Start);
            }

            if (note.Start < span.End && span.End <= note.End)
            {
                last = Math.Min(last, note.End);
            }
        }

        for (int at = 1; at < path.Length; at++)
        {
            if (!SpcPitchPath.AreDifferentNotes(path[at - 1].Value, path[at].Value))
            {
                continue;
            }

            if (path[at].Frame <= span.Start)
            {
                first = Math.Max(first, path[at].Frame);
                continue;
            }

            if (path[at].Frame >= span.End)
            {
                last = Math.Min(last, path[at].Frame);
                break;
            }
        }

        return (first, last);
    }

    // The value a span swung about at every poll from half a cycle before it to half a cycle after it, as the pitch averaged over the cycle around the
    // poll, where that cycle lies inside the frames it may reach to.
    private static List<Center> CentersAcross(HeardSemitones heard, SwingSpan span, (long First, long Last) reach)
    {
        var centers = new List<Center>();
        double halfCycle = span.CycleFrames / 2;
        double lastCenterFrame = Math.Min(span.End + halfCycle, reach.Last - halfCycle);
        for (double frame = Math.Max(span.Start - halfCycle, reach.First + halfCycle); frame <= lastCenterFrame; frame += SpcTrace.PollFrames)
        {
            centers.Add(new Center(frame, heard.AverageBetween(frame - halfCycle, frame + halfCycle)));
        }

        return centers;
    }

    // Adds each move of the value a span swung about that left where it stood and settled somewhere else far enough away to be a slide.
    private static void AddDrifts(ImmutableArray<SpcTraceWalk>.Builder drifts, IReadOnlyList<SpcTracePoint> path, List<Center> centers, double cycleFrames)
    {
        if (centers.Count == 0)
        {
            return;
        }

        int heldIndex = 0;
        for (int at = 1; at < centers.Count; at++)
        {
            if (Math.Abs(centers[at].Semitones - centers[heldIndex].Semitones) <= HoldToleranceSemitones)
            {
                continue;
            }

            int settledIndex = SettledFrom(centers, at, cycleFrames);
            if (Math.Abs(centers[settledIndex].Semitones - centers[heldIndex].Semitones) >= MinDriftSemitones)
            {
                drifts.Add(DriftOf(path, centers, heldIndex, settledIndex));
            }

            heldIndex = settledIndex;
            at = settledIndex;
        }
    }

    // The first center from one on that every center for a cycle after it stays near, or the last center where none does.
    private static int SettledFrom(List<Center> centers, int from, double cycleFrames)
    {
        for (int at = from; at < centers.Count; at++)
        {
            bool isSettled = true;
            for (int after = at + 1; after < centers.Count && centers[after].Frame - centers[at].Frame <= cycleFrames; after++)
            {
                if (Math.Abs(centers[after].Semitones - centers[at].Semitones) > HoldToleranceSemitones)
                {
                    isSettled = false;
                    break;
                }
            }

            if (isSettled)
            {
                return at;
            }
        }

        return centers.Count - 1;
    }

    // The walk a move from one held center to a settled one makes, as the straight ramp through where it passed a quarter and three quarters of the way.
    private static SpcTraceWalk DriftOf(IReadOnlyList<SpcTracePoint> path, List<Center> centers, int heldIndex, int settledIndex)
    {
        double from = centers[heldIndex].Semitones;
        double to = centers[settledIndex].Semitones;

        // The frames the move passed a quarter and three quarters of the way.
        double quarterFrame = PassingFrame(centers, heldIndex, settledIndex, from + (to - from) * MoveQuarter);
        double threeQuartersFrame = PassingFrame(centers, heldIndex, settledIndex, from + (to - from) * MoveThreeQuarters);

        double halfRampFrames = threeQuartersFrame - quarterFrame;

        // The ramp's two ends, each extended by a quarter of the ramp but held between the two centers.
        long start = (long)Math.Round(Math.Max(quarterFrame - halfRampFrames / 2, centers[heldIndex].Frame));
        long end = (long)Math.Round(Math.Min(threeQuartersFrame + halfRampFrames / 2, centers[settledIndex].Frame));

        int rampWriteCount = path.Count(point => point.Frame >= start && point.Frame < end);
        return new SpcTraceWalk(SpcPitchSwings.RegisterOf(from), SpcPitchSwings.RegisterOf(to), start, Math.Max(end, start + 1), rampWriteCount, IsUnderSwing: true);
    }

    // The frame the centers from one to another first reach a value, between the two either side of it.
    private static double PassingFrame(List<Center> centers, int first, int last, double value)
    {
        bool isRising = centers[last].Semitones > centers[first].Semitones;
        for (int at = first + 1; at <= last; at++)
        {
            if (centers[at].Semitones >= value != isRising)
            {
                continue;
            }

            var before = centers[at - 1];
            double riseFromBefore = centers[at].Semitones - before.Semitones;
            double fractionBetweenCenters = riseFromBefore == 0 ? 1 : Math.Clamp((value - before.Semitones) / riseFromBefore, 0, 1);
            return before.Frame + (centers[at].Frame - before.Frame) * fractionBetweenCenters;
        }

        return centers[last].Frame;
    }

    // A run of swings without a break, from one frame to another, at one speed.
    private readonly record struct SwingSpan(long Start, long End, double CycleFrames);

    // The value a swing was about at one frame, in semitones.
    private readonly record struct Center(double Frame, double Semitones);

    // A path's values in semitones held from each one's frame to the next one's, read back as their average across any stretch of frames.
    private sealed class HeardSemitones
    {
        private readonly long[] _frames;
        private readonly double[] _semitones;
        private readonly double[] _sumBefore;

        // A path's values, the last held until the block's end.
        public HeardSemitones(IReadOnlyList<SpcTracePoint> path, long end)
        {
            _frames = [.. path.Select(point => point.Frame)];
            _semitones = [.. path.Select(point => SpcPitchSwings.SemitonesOf(point.Value))];
            _sumBefore = new double[path.Count + 1];
            for (int at = 0; at < path.Count; at++)
            {
                long heldUntilFrame = at + 1 < path.Count ? _frames[at + 1] : end;
                _sumBefore[at + 1] = _sumBefore[at] + _semitones[at] * (heldUntilFrame - _frames[at]);
            }
        }

        // The value held on average from one frame to another.
        public double AverageBetween(double fromFrame, double toFrame)
        {
            return (SumUpTo(toFrame) - SumUpTo(fromFrame)) / (toFrame - fromFrame);
        }

        // The values held from the path's first frame up to another, each weighted by how long it was held.
        private double SumUpTo(double frame)
        {
            int inForceIndex = Array.BinarySearch(_frames, (long)Math.Floor(frame));
            inForceIndex = inForceIndex >= 0 ? inForceIndex : Math.Max(~inForceIndex - 1, 0);
            return _sumBefore[inForceIndex] + _semitones[inForceIndex] * (frame - _frames[inForceIndex]);
        }
    }
}
