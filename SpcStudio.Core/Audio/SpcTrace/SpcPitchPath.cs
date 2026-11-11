using System.Collections.Immutable;

namespace SpcStudio.Audio;

// A block's record of PITCH values read as music: the notes it sounded, wherever the pitch jumped and stayed, and the runs it walked between them, neither of which a vibrato is.
public static class SpcPitchPath
{
    private const long SettleFrames = 2 * SpcTrace.PollFrames;
    private const long OnsetFrames = 4 * SpcTrace.PollFrames;
    private const long SlideStepFrames = 4 * SpcTrace.PollFrames;
    private const double NotesApartLogRatio = 0.0289;

    // A block's pitch path as the notes it sounded, a new one wherever the pitch jumped and stayed outside its vibratos, never empty, a note read inside one standing at the value it swung about.
    public static ImmutableArray<SpcTraceNote> NotesOf(IReadOnlyList<SpcTracePoint> path, long end, ImmutableArray<SpcTraceSwing> vibratos)
    {
        var notes = ImmutableArray.CreateBuilder<SpcTraceNote>();
        var settled = SettledValues(path, end);
        var swept = SpcSwings.SweptValues(path, vibratos);
        int onsetEnd = OnsetEnd(path, settled);
        long start = path[0].Frame;
        int pitch = SpcPitchSwings.PitchAt(vibratos, path[onsetEnd].Frame, path[onsetEnd].Value);
        for (int at = onsetEnd + 1; at < path.Count; at++)
        {
            if (swept[at] || !StartsNewNote(path, settled, end, at, pitch))
            {
                continue;
            }

            if (path[at].Frame <= start || path[at].Frame >= end)
            {
                continue;
            }

            notes.Add(new SpcTraceNote(pitch, start, path[at].Frame));
            start = path[at].Frame;
            pitch = path[at].Value;
        }

        notes.Add(new SpcTraceNote(pitch, start, Math.Max(end, start + 1)));
        return notes.ToImmutable();
    }

    // A block's pitch path as the runs it walked, in the order it walked them: every slide the value a vibrato swung about made while it swung, and every run of values the notes and the vibratos did
    // not take, outside the onset, that left a note or arrived at one - of a slide and a run that overlap, only the one that moved further.
    public static ImmutableArray<SpcTraceWalk> WalksOf(SpcTraceBlock block)
    {
        var path = block.Path;
        if (path.Length < 2)
        {
            return [];
        }

        var walks = ImmutableArray.CreateBuilder<SpcTraceWalk>();
        var settled = SettledValues(path, block.End);
        var swept = SpcSwings.SweptValues(path, block.Vibratos);
        int onsetEnd = OnsetEnd(path, settled);
        for (int at = 0; at < path.Length; at++)
        {
            if (settled[at] || swept[at])
            {
                continue;
            }

            int last = at;
            while (last + 1 < path.Length && !settled[last + 1] && !swept[last + 1])
            {
                last++;
            }

            if (onsetEnd <= last && WalkOf(block, at, last, onsetEnd) is { } walk)
            {
                walks.Add(walk);
            }

            at = last;
        }

        var drifts = SpcPitchDrifts.DriftsOf(block);
        var keptWalks = walks.Where(walk => !drifts.Any(drift => AreWalkingAtOnce(walk, drift) && LogRatioMoved(drift) > LogRatioMoved(walk)));
        var keptDrifts = drifts.Where(drift => !walks.Any(walk => AreWalkingAtOnce(walk, drift) && LogRatioMoved(walk) >= LogRatioMoved(drift)));
        return [.. keptWalks.Concat(keptDrifts).OrderBy(walk => walk.Start)];
    }

    // Whether two walks were walking at once.
    private static bool AreWalkingAtOnce(SpcTraceWalk walk, SpcTraceWalk other)
    {
        return walk.Start < other.End && other.Start < walk.End;
    }

    // How far a walk moved, as a natural log of the ratio between where it ended and where it started.
    private static double LogRatioMoved(SpcTraceWalk walk)
    {
        return Math.Abs(Math.Log((double)Math.Max(walk.To, 1) / Math.Max(walk.From, 1)));
    }

    // The walk a run of unsettled values from one index to another comes to, or null where it neither left a note, arrived at one nor moved one way.
    private static SpcTraceWalk? WalkOf(SpcTraceBlock block, int first, int last, int onsetEnd)
    {
        var path = block.Path;
        bool hasLeftNote = first > onsetEnd;
        bool isFollowed = last + 1 < path.Length;
        bool isCutByNote = isFollowed && block.Notes.Skip(1).Any(note => note.Start == path[last + 1].Frame);
        bool hasArrived = isFollowed && !isCutByNote;
        if (!hasLeftNote && !hasArrived && !IsOneWaySlide(path, first, last))
        {
            return null;
        }

        int from = hasLeftNote ? path[first - 1].Value : path[first].Value;
        int arrivalIndex = hasArrived ? ArrivalIndex(path, block.Vibratos, last + 1, from) : last + 1;
        return new SpcTraceWalk(
            from,
            hasArrived ? SpcPitchSwings.PitchAt(block.Vibratos, path[last + 1].Frame, path[last + 1].Value) : path[last].Value,
            path[first].Frame,
            isFollowed ? path[arrivalIndex].Frame : Math.Max(block.End, path[last].Frame + 1),
            arrivalIndex - first,
            IsUnderSwing: false);
    }

    // The index of the value a walk from a pitch arrived at, given the value that followed it: that value, or where that value lies inside a vibrato, the
    // first of the vibrato's values to reach the value it swung about from the walk's side, the following value again where none did.
    private static int ArrivalIndex(IReadOnlyList<SpcTracePoint> path, ImmutableArray<SpcTraceSwing> vibratos, int following, int from)
    {
        if (SpcSwings.SwingAt(vibratos, path[following].Frame) is not { } vibrato)
        {
            return following;
        }

        bool isRising = vibrato.Center > from;
        for (int at = following; at < path.Count && vibrato.IsSwingingAt(path[at].Frame); at++)
        {
            if (isRising ? path[at].Value >= vibrato.Center : path[at].Value <= vibrato.Center)
            {
                return at;
            }
        }

        return following;
    }

    // Whether the note at a pitch ends at a value of a path and another starts there, which is never true of the path's first value.
    private static bool StartsNewNote(IReadOnlyList<SpcTracePoint> path, bool[] settled, long end, int at, int pitch)
    {
        if (!settled[at] || !AreDifferentNotes(path[at].Value, pitch))
        {
            return false;
        }

        if (settled[at - 1])
        {
            return true;
        }

        long stood = (at + 1 < path.Count ? path[at + 1].Frame : end) - path[at].Frame;
        return AreDifferentNotes(path[at - 1].Value, path[at].Value) && !AreDifferentNotes(path[at - 1].Value, pitch) && stood > SlideStepFrames;
    }

    // Whether each value of a path was held as a note's own pitch: for long enough, and not as a step in the middle of a slide.
    private static bool[] SettledValues(IReadOnlyList<SpcTracePoint> path, long end)
    {
        var settled = new bool[path.Count];
        for (int at = 0; at < path.Count; at++)
        {
            long held = (at + 1 < path.Count ? path[at + 1].Frame : end) - path[at].Frame;
            bool isSlideStep = held < SlideStepFrames && at > 0 && at + 1 < path.Count && IsOneWaySlide(path, at - 1, at + 1);
            settled[at] = held >= SettleFrames && !isSlideStep;
        }

        return settled;
    }

    // The index of the last value that settled within the onset after the key-on, or zero where none did.
    private static int OnsetEnd(IReadOnlyList<SpcTracePoint> path, bool[] settled)
    {
        int onsetEnd = 0;
        for (int at = 1; at < path.Count && path[at].Frame - path[0].Frame < OnsetFrames; at++)
        {
            if (settled[at])
            {
                onsetEnd = at;
            }
        }

        return onsetEnd;
    }

    // Whether the values from one index to another are a slide: every move between them a small one, and all of them the same way.
    private static bool IsOneWaySlide(IReadOnlyList<SpcTracePoint> path, int first, int last)
    {
        if (last <= first)
        {
            return false;
        }

        bool isRising = path[first + 1].Value > path[first].Value;
        for (int at = first + 1; at <= last; at++)
        {
            if (!IsSlideStep(path[at - 1].Value, path[at].Value) || path[at].Value > path[at - 1].Value != isRising)
            {
                return false;
            }
        }

        return true;
    }

    // Whether a move between two values is one step of a slide: a move, and a small one.
    private static bool IsSlideStep(int from, int to)
    {
        return from != to && !AreDifferentNotes(from, to);
    }

    // Whether two pitch registers are far enough apart to be different notes.
    internal static bool AreDifferentNotes(int pitch, int other)
    {
        return pitch > 0 && other > 0 && Math.Abs(Math.Log((double)pitch / other)) > NotesApartLogRatio;
    }
}
