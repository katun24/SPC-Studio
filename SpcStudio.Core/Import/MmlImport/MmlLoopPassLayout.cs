namespace SpcStudio.Import;

// Lays out a song whose channels loop from different points the way the driver plays it: every channel's intro up to where the first channel runs out,
// then one pass of the loop with each channel carried on from its own last /, every channel looping from the intro's end.
internal static class MmlLoopPassLayout
{
    private const int StreamStartIndex = 0;
    private const int StreamStartTick = 0;

    // Where a channel's loop pass starts in its stream: the index of the event after its last /, that /'s tick and its line.
    private readonly record struct LoopStart(int EventIndex, int Tick, int Line);

    // The file laid out as the driver plays it, or the file itself where every channel loops from one tick.
    public static MmlFile LaidOut(MmlFile file)
    {
        var loopStarts = file.Channels.Select(LoopStartOf).ToList();
        if (!file.DoesLoop || loopStarts.All(loopStart => loopStart.Tick == loopStarts[0].Tick))
        {
            return file;
        }

        int introEndTick = file.Channels.Min(channel => channel.EndTick);
        return file with { Channels = [.. file.Channels.Zip(loopStarts, (channel, loopStart) => LaidOut(channel, loopStart, introEndTick))] };
    }

    // Where a channel's loop pass starts, the stream's own start where it wrote no /.
    private static LoopStart LoopStartOf(MmlChannel channel)
    {
        int markIndex = channel.Events.ToList().FindLastIndex(channelEvent => channelEvent is MmlLoopMark);
        if (markIndex < 0)
        {
            return new(StreamStartIndex, StreamStartTick, RomTrackReader.NoTextLine);
        }

        var mark = channel.Events[markIndex];
        return new(markIndex + 1, mark.Tick, mark.Line);
    }

    // One channel laid out: its stream up to the intro's end, a / there, and its stream from its own / on moved to start there, each of its compiled
    // bytes' facts going with the part it belongs to. A channel whose / stands at the intro's end plays straight on, and is left as it is.
    private static MmlChannel LaidOut(MmlChannel channel, LoopStart loopStart, int introEndTick)
    {
        int movedTicks = introEndTick - loopStart.Tick;
        if (movedTicks == 0)
        {
            return channel;
        }

        var noteAcrossIntroEnd = NoteAcross(channel, introEndTick);
        var openingTiePieces = NoteAcross(channel, loopStart.Tick) is { } noteAcrossLoop ? noteAcrossLoop.PiecesSplitAt(loopStart.Tick - noteAcrossLoop.Tick).After : [];
        var loopEvents = channel.Events.Skip(loopStart.EventIndex).Select(channelEvent => channelEvent with { Tick = channelEvent.Tick + movedTicks });
        var introLoopEdges = channel.LoopEdges.Where(edge => edge.Tick <= introEndTick).ToList();
        var loopPassLoopEdges = channel.LoopEdges.Where(edge => edge.Tick > loopStart.Tick).Select(edge => edge with { Tick = edge.Tick + movedTicks });

        // Whether a note sounding across the intro's end rings on through the tie bytes the loop pass opens with, nothing keying it off at the jump.
        bool isNoteCarriedOn = noteAcrossIntroEnd is not null && openingTiePieces.Length > 0;

        // Whether the stream's next byte at the intro's end is not the one the loop pass plays next, the driver jumping to the / there.
        bool isJumpAtIntroEnd = introLoopEdges.All(edge => edge.Tick != introEndTick);

        var introEvents = channel.Events
            .Where(channelEvent => channelEvent is not MmlLoopMark && channelEvent.Tick < introEndTick)
            .Select(channelEvent => channelEvent is MmlNote note && note == noteAcrossIntroEnd ? NoteAtIntroEnd(note, introEndTick, isNoteCarriedOn ? openingTiePieces : []) : channelEvent);
        List<MmlLoopEdge> seamLoopEdges = isJumpAtIntroEnd ? [new(introEndTick, SectionEnds.ReadAheadStopAt(noteAcrossIntroEnd))] : [];
        List<int> openingRestTieTicks = isNoteCarriedOn ? [] : TieByteStartTicks(openingTiePieces, loopStart.Tick);
        return channel with
        {
            Events = [.. introEvents, new MmlLoopMark(introEndTick, loopStart.Line), .. loopEvents],
            EndTick = channel.EndTick + movedTicks,
            CutTies =
            [
                .. channel.CutTies.Where(tie => tie.Tick < introEndTick).Select(tie => tie with { LengthTicks = Math.Min(tie.LengthTicks, introEndTick - tie.Tick) }),
                .. channel.CutTies.Where(tie => tie.Tick >= loopStart.Tick).Select(tie => tie with { Tick = tie.Tick + movedTicks }),
            ],
            LoopEdges = [.. introLoopEdges.Concat(seamLoopEdges).Concat(loopPassLoopEdges).Distinct().OrderBy(edge => edge.Tick)],
            RestBytes = LaidOutTicks(channel.RestBytes, loopStart.Tick, introEndTick),
            RestTies = LaidOutTicks(channel.RestTies.Concat(openingRestTieTicks), loopStart.Tick, introEndTick),
        };
    }

    // The note of a channel's stream sounding across a tick, starting before it and ending after it, or null where none does.
    private static MmlNote? NoteAcross(MmlChannel channel, int tick)
    {
        return channel.Events.OfType<MmlNote>().FirstOrDefault(note => note.Tick < tick && note.EndTick > tick);
    }

    // A note sounding across the intro's end as the driver plays it there: cut at the jump, and carried on through the tie bytes given.
    private static MmlNote NoteAtIntroEnd(MmlNote note, int introEndTick, IReadOnlyCollection<int> carryingTiePieces)
    {
        return SectionEnds.CutAt(note, introEndTick).LengthenedBy(carryingTiePieces);
    }

    // The ticks a channel's bytes start at, laid out: those in front of the intro's end where they stand, and those from the / on moved with the loop pass.
    private static List<int> LaidOutTicks(IEnumerable<int> byteStartTicks, int loopTick, int introEndTick)
    {
        var ticks = byteStartTicks.ToList();
        var introTicks = ticks.Where(tick => tick < introEndTick);
        var loopPassTicks = ticks.Where(tick => tick >= loopTick).Select(tick => tick + introEndTick - loopTick);
        return [.. introTicks.Concat(loopPassTicks).Distinct().Order()];
    }

    // The ticks tie bytes of those lengths start at, laid end to end from a tick.
    private static List<int> TieByteStartTicks(IEnumerable<int> tiePieces, int firstTick)
    {
        var tieTicks = new List<int>();
        int tieTick = firstTick;
        foreach (int pieceTicks in tiePieces)
        {
            tieTicks.Add(tieTick);
            tieTick += pieceTicks;
        }

        return tieTicks;
    }
}
