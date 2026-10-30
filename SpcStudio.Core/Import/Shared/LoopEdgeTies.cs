using SpcStudio.Export;

namespace SpcStudio.Import;

// Decides, once a song is read, which of the ties at its loops' edges are rests, for the text parser and the ROM reader alike.
internal static class LoopEdgeTies
{
    // The song with every channel's loop-edge ties decided.
    public static MmlFile Resolve(MmlFile file)
    {
        if (file.Channels.All(channel => channel.CutTies.Count == 0))
        {
            return file;
        }

        return file with { Channels = [.. file.Channels.Select(Resolve)] };
    }

    // One channel with each of its loop-edge ties that the note in front cannot carry on through cut back into rest bytes.
    private static MmlChannel Resolve(MmlChannel channel)
    {
        if (channel.CutTies.Count == 0)
        {
            return channel;
        }

        var events = channel.Events.ToList();
        var readAheadStops = new Dictionary<int, ReadAheadStop>();
        foreach (var loopEdge in channel.LoopEdges)
        {
            readAheadStops.TryAdd(loopEdge.Tick, loopEdge.ReadAheadStop);
        }

        List<MmlCutTie> resolvedTies = [];
        var cutTieTicks = new HashSet<int>();
        foreach (var cutTie in channel.CutTies.OrderBy(tie => tie.Tick))
        {
            int noteIndex = events.FindIndex(e => e is MmlNote note && note.Tick < cutTie.Tick && note.EndTick > cutTie.Tick);
            if (noteIndex < 0)
            {
                if (!cutTieTicks.Contains(cutTie.Tick))
                {
                    resolvedTies.Add(cutTie);
                }

                continue;
            }

            var note = (MmlNote)events[noteIndex];
            int keptTicks = cutTie.Tick - note.Tick;
            if (IsNoteCarriedOn(events, readAheadStops, note, keptTicks))
            {
                resolvedTies.Add(cutTie);
                continue;
            }

            List<MmlRestBend> bendsCarriedBehindRestBytes = [];
            int restByteTick = cutTie.Tick;
            foreach (int pieceTicks in TailPieces(note, keptTicks))
            {
                resolvedTies.Add(cutTie with { Tick = restByteTick, LengthTicks = pieceTicks });
                cutTieTicks.Add(restByteTick);
                foreach (var bend in note.Bends)
                {
                    if (note.Tick + bend.ArmTicks == restByteTick)
                    {
                        bendsCarriedBehindRestBytes.Add(new MmlRestBend(restByteTick, bend.DelayTicks, bend.LengthTicks, bend.TargetNoteValue, cutTie.Line));
                    }
                }

                restByteTick += pieceTicks;
            }

            if (bendsCarriedBehindRestBytes.Count != note.Bends.Count(bend => bend.ArmTicks >= keptTicks))
            {
                throw MmlImportException.BendAfterKeyedOffTie(cutTie.Line);
            }

            events[noteIndex] = note.ShortenedTo(keptTicks) with { Bends = [.. note.Bends.Where(bend => bend.ArmTicks < keptTicks)] };
            foreach (var bend in bendsCarriedBehindRestBytes)
            {
                int insertIndex = events.FindIndex(noteIndex + 1, e => e.Tick > bend.Tick);
                events.Insert(insertIndex < 0 ? events.Count : insertIndex, bend);
            }
        }

        return channel with { Events = events, CutTies = [.. resolvedTies.Distinct().OrderBy(tie => tie.Tick)] };
    }

    // Whether the note's byte ending that many ticks in skips its cuts, so the note sounds on into the tie that follows.
    private static bool IsNoteCarriedOn(List<MmlEvent> events, Dictionary<int, ReadAheadStop> readAheadStops, MmlNote note, int keptTicks)
    {
        int byteEndTick = note.Tick + keptTicks;
        return readAheadStops.GetValueOrDefault(byteEndTick, ReadAheadStop.Tie) == ReadAheadStop.Tie
            || LegatoBits.IsBitSetBefore(events, byteEndTick)
            || note.PieceTicksEndingAt(keptTicks) == 1;
    }

    // The note's tie bytes from that many ticks in to its end, as AddmusicK would split them where the note's own byte layout no longer says.
    private static IEnumerable<int> TailPieces(MmlNote note, int keptTicks)
    {
        if (note.HasKnownLayout)
        {
            return note.PiecesSplitAt(keptTicks).After;
        }

        return MmlDurationEncoding.SplitRunIntoTiedPieces(note.LengthTicks - keptTicks);
    }
}
