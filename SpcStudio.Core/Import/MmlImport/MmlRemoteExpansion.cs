using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

// Turns a channel's remote code calls into ordinary events at the ticks they would have fired at, so that everything downstream sees a song with no remote code in it. Split over four files: this one
// holds the entry point and what other import steps share, the others the channel walk, the timer's two types and the key-off's.
internal static partial class MmlRemoteExpansion
{
    // Every channel with its remote code expanded away, the song-wide wait time timeline built once for all of them.
    public static IReadOnlyList<MmlChannel> ExpandChannels(MmlFile file, Action<string>? warn)
    {
        if (!file.Channels.Any(channel => channel.Events.Any(channelEvent => channelEvent is MmlRemoteCall)))
        {
            return file.Channels;
        }

        var waitTimeAt = WaitTimeLookup(file);
        return [.. file.Channels.Select(channel => new ChannelWalk(channel, file.RemoteBlocks, file.DoesLoop, waitTimeAt, warn).Run())];
    }

    // The driver's wait time at any tick, read off every channel's light staccato toggles at once, two toggles at one tick cancelling.
    internal static Func<int, int> WaitTimeLookup(MmlFile file)
    {
        List<(int Tick, int WaitTime)> changes = [];
        int waitTime = SnesTiming.DefaultWaitTime;
        var staccatoTogglesByTick = file.Channels.SelectMany(channel => channel.Events.OfType<MmlStaccato>()).GroupBy(toggle => toggle.Tick).OrderBy(group => group.Key);
        foreach (var togglesAtTick in staccatoTogglesByTick)
        {
            if (togglesAtTick.Count() % 2 == 0)
            {
                continue;
            }

            waitTime = waitTime == SnesTiming.DefaultWaitTime ? SnesTiming.StaccatoWaitTime : SnesTiming.DefaultWaitTime;
            changes.Add((togglesAtTick.Key, waitTime));
        }

        if (changes.Count == 0)
        {
            return _ => SnesTiming.DefaultWaitTime;
        }

        return tick =>
        {
            int changeCount = SortedSearch.CountLeading(changes, change => change.Tick <= tick);
            return changeCount == 0 ? SnesTiming.DefaultWaitTime : changes[changeCount - 1].WaitTime;
        };
    }

    // The ticks each byte of a rest covers, in order, put back together from everything that parts two of them.
    internal static IEnumerable<int> RestPieces(MmlChannel channel, int eventIndex, RestSplits restSplits, int fromTick, int toTick)
    {
        var cutTicks = new SortedSet<int> { toTick };
        foreach (var (_, byteEndTick) in restSplits.ByteRuns(fromTick, toTick))
        {
            cutTicks.Add(byteEndTick);
        }

        foreach (int tieTick in channel.RestTies)
        {
            if (tieTick > fromTick && tieTick < toTick)
            {
                cutTicks.Add(tieTick);
            }
        }

        for (int laterIndex = eventIndex + 1; laterIndex < channel.Events.Count; laterIndex++)
        {
            int eventTick = channel.Events[laterIndex].Tick;
            if (eventTick >= toTick)
            {
                break;
            }

            if (eventTick > fromTick)
            {
                cutTicks.Add(eventTick);
            }
        }

        int runStartTick = fromTick;
        foreach (int cutTick in cutTicks.Where(cutTick => cutTick > fromTick))
        {
            foreach (int pieceTicks in MmlDurationEncoding.SplitRunIntoTiedPieces(cutTick - runStartTick))
            {
                yield return pieceTicks;
            }

            runStartTick = cutTick;
        }
    }

    // The furthest tick the note at an index may be written out to without swallowing whatever comes after it.
    internal static int FurthestGrowthTick(IReadOnlyList<MmlEvent> events, int noteIndex, int noteEndTick, int channelEndTick)
    {
        for (int laterIndex = noteIndex + 1; laterIndex < events.Count; laterIndex++)
        {
            if (events[laterIndex].Tick >= noteEndTick)
            {
                return Math.Max(noteEndTick, events[laterIndex].Tick);
            }
        }

        return Math.Max(noteEndTick, channelEndTick);
    }

    // How far a note may grow when the next note starts under the legato bit: a tick short of it, or null where the two already touch.
    internal static int? FurthestGrowthTickUnderLegato(IReadOnlyList<MmlEvent> events, int noteIndex, int noteEndTick, int channelEndTick, int? followingNoteTick)
    {
        if (followingNoteTick == noteEndTick)
        {
            return null;
        }

        int growthTick = FurthestGrowthTick(events, noteIndex, noteEndTick, channelEndTick);
        return followingNoteTick is int followingTick ? Math.Max(noteEndTick, Math.Min(growthTick, followingTick - 1)) : growthTick;
    }

    // Whether a block writes nothing but the envelope.
    private static bool IsEnvelopeOnly(IReadOnlyList<MmlEvent> body)
    {
        return body.All(blockEvent => blockEvent is MmlEnvelope or MmlEnvelopeGain or MmlEnvelopeRestore or MmlRemoteGain);
    }

    // The length of the note's last byte, which both of its cuts are made in: its own byte layout where that holds, otherwise wherever the events
    // inside the note split its run, and null where nothing does.
    private static int? LastByteTicks(MmlChannel channel, int noteIndex, MmlNote note)
    {
        return note.LastPieceTicks ?? FinalPieceTicksSplitByEvents(channel, noteIndex, note);
    }

    // The length of the note's last duration byte where the events inside the note split its run, or null where nothing does.
    private static int? FinalPieceTicksSplitByEvents(MmlChannel channel, int noteIndex, MmlNote note)
    {
        List<int> insideEventTicks = [.. EventsInsideNote(channel, noteIndex, note).Select(insideEvent => insideEvent.Tick)];
        return insideEventTicks.Count == 0 ? null : SnesTiming.FinalPieceTicks(note.Tick, note.LengthTicks, insideEventTicks);
    }

    // The events after a note's key-on and before its end.
    private static IEnumerable<MmlEvent> EventsInsideNote(MmlChannel channel, int noteIndex, MmlNote note)
    {
        int noteEndTick = note.EndTick;
        for (int laterIndex = noteIndex + 1; laterIndex < channel.Events.Count && channel.Events[laterIndex].Tick < noteEndTick; laterIndex++)
        {
            if (channel.Events[laterIndex].Tick > note.Tick)
            {
                yield return channel.Events[laterIndex];
            }
        }
    }

    // The last note in front of an event, or null where the channel has none there.
    private static MmlNote? LastNoteBefore(MmlChannel channel, int eventIndex)
    {
        for (int earlierIndex = eventIndex - 1; earlierIndex >= 0; earlierIndex--)
        {
            if (channel.Events[earlierIndex] is MmlNote note)
            {
                return note;
            }
        }

        return null;
    }

    // Adds a block's events at a tick, where there is a tick to place them on.
    private static void PlaceBlockAt(int? tick, IReadOnlyList<MmlEvent> body, List<MmlEvent> output)
    {
        if (tick is { } fireTick)
        {
            output.AddRange(StampedAt(body, fireTick));
        }
    }

    // The body of the block a call arms.
    private static IReadOnlyList<MmlEvent> BlockBody(IReadOnlyDictionary<int, IReadOnlyList<MmlEvent>> blocks, MmlRemoteCall call)
    {
        return BlockBody(blocks, call.BlockLabel ?? -1, call.Line);
    }

    // The body of the block under a label, refused where no block has that label.
    private static IReadOnlyList<MmlEvent> BlockBody(IReadOnlyDictionary<int, IReadOnlyList<MmlEvent>> blocks, int label, int line)
    {
        return blocks.TryGetValue(label, out var body) ? body : throw MmlImportException.RemoteBlockUndefined(label, line);
    }

    // A block's events, stamped onto the tick they fire at.
    private static IEnumerable<MmlEvent> StampedAt(IReadOnlyList<MmlEvent> body, int tick)
    {
        return body.Select(blockEvent => blockEvent with { Tick = tick });
    }
}
