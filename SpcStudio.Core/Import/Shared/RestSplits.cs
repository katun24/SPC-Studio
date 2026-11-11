namespace SpcStudio.Import;

// Where a channel's rests are split into the bytes the driver cuts in, and what the read-ahead finds at each of the channel's loop edges.
internal sealed class RestSplits
{
    private readonly SortedSet<int> _byteBoundaryTicks = [];
    private readonly HashSet<int> _tieStartTicks = [];
    private readonly Dictionary<int, int> _tieAfterNoteLines = [];
    private readonly Dictionary<int, ReadAheadStop> _edgeReadAheadStops = [];

    // The splits of one channel's rests, read off its cut ties, its loop edges and its rest bytes.
    public static RestSplits ForChannel(MmlChannel channel)
    {
        var splits = new RestSplits();
        foreach (var tie in channel.CutTies)
        {
            splits._tieStartTicks.Add(tie.Tick);
            splits._byteBoundaryTicks.Add(tie.Tick);
            splits._byteBoundaryTicks.Add(tie.EndTick);
            if (!tie.IsAfterRest)
            {
                splits._tieAfterNoteLines.TryAdd(tie.Tick, tie.Line);
            }
        }

        foreach (var edge in channel.LoopEdges)
        {
            splits._edgeReadAheadStops.TryAdd(edge.Tick, edge.ReadAheadStop);
            splits._byteBoundaryTicks.Add(edge.Tick);
        }

        foreach (int restByteTick in channel.RestBytes)
        {
            splits._byteBoundaryTicks.Add(restByteTick);
        }

        return splits;
    }

    // Whether a tie byte is fetched at a tick.
    public bool DoesStartTie(int tick)
    {
        return _tieStartTicks.Contains(tick);
    }

    // The line of a tie read as a rest that is fetched at a tick straight after a note, with no rest byte in front of it, or null where there is none.
    public int? TieAfterNoteLine(int tick)
    {
        return _tieAfterNoteLines.TryGetValue(tick, out int line) ? line : null;
    }

    // What the read-ahead from the byte ending at a tick finds, where a loop edge makes that something other than the byte played next.
    public ReadAheadStop? EdgeReadAheadStopAt(int tick)
    {
        return _edgeReadAheadStops.TryGetValue(tick, out var stop) ? stop : null;
    }

    // Whether the byte ending at a tick skips its cuts, the read-ahead having found a tie past a loop edge there.
    public bool IsHeldAt(int tick)
    {
        return EdgeReadAheadStopAt(tick) == ReadAheadStop.Tie;
    }

    // The first byte boundary after a tick, or the run's end where none stands in front of it.
    public int NextByteBoundary(int fromTick, int toTick)
    {
        foreach (int boundaryTick in _byteBoundaryTicks.GetViewBetween(fromTick + 1, Math.Max(fromTick + 1, toTick)))
        {
            return Math.Min(boundaryTick, toTick);
        }

        return toTick;
    }

    // The run between two ticks, byte by byte.
    public IEnumerable<(int FromTick, int ToTick)> ByteRuns(int fromTick, int toTick)
    {
        while (fromTick < toTick)
        {
            int nextTick = NextByteBoundary(fromTick, toTick);
            yield return (fromTick, nextTick);
            fromTick = nextTick;
        }
    }
}
