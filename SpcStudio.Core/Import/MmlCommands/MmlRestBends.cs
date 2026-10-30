using System.Collections.Immutable;
using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

// Puts every pitch bend written in a rest where the driver plays it: on the chain of the note in front of it, read during the rest behind that note,
// and nowhere where no note has keyed on in front of it.
internal static class MmlRestBends
{
    private const int BeforeFirstEventIndex = -1;
    private const int ChannelStartTick = 0;
    private const int NoNoteTicks = 0;

    // Every channel with its rest bends taken off the stream and held beside it.
    public static MmlFile LiftOffStreams(MmlFile file)
    {
        if (file.Channels.All(channel => !channel.Events.Any(channelEvent => channelEvent is MmlRestBend)))
        {
            return file;
        }

        return file with
        {
            Channels =
            [
                .. file.Channels.Select(channel => channel with
                {
                    Events = [.. channel.Events.Where(channelEvent => channelEvent is not MmlRestBend)],
                    RestBends = [.. channel.Events.OfType<MmlRestBend>()],
                }),
            ],
        };
    }

    // Every channel with its rest bends placed.
    public static IReadOnlyList<MmlChannel> PlaceOnNotes(IReadOnlyList<MmlChannel> channels)
    {
        return [.. channels.Select(PlaceChannelBends)];
    }

    // One channel with its rest bends placed on the notes in front of them, the ones with no note in front refused where the driver never reads them.
    private static MmlChannel PlaceChannelBends(MmlChannel channel)
    {
        if (channel.RestBends.Count == 0)
        {
            return channel;
        }

        var restSplits = RestSplits.ForChannel(channel);
        var events = channel.Events.ToList();
        List<MmlRestBend> bendsBeforeFirstNote = [];
        foreach (var bend in channel.RestBends)
        {
            int noteBeforeBendIndex = events.FindLastIndex(channelEvent => channelEvent is MmlNote && channelEvent.Tick < bend.Tick);
            if (noteBeforeBendIndex < 0)
            {
                bendsBeforeFirstNote.Add(bend);
                continue;
            }

            var note = (MmlNote)events[noteBeforeBendIndex];
            if (bend.LengthTicks == MmlPitchBend.NoRampTicks)
            {
                if (note.EndTick > bend.Tick)
                {
                    int firstEventPastBendIndex = events.FindIndex(noteBeforeBendIndex + 1, channelEvent => channelEvent.Tick > bend.Tick);
                    events.Insert(firstEventPastBendIndex < 0 ? events.Count : firstEventPastBendIndex, new MmlIgnored(bend.Tick, IgnoredCommand.ZeroLengthPitchBend, bend.Line));
                }

                continue;
            }

            events[noteBeforeBendIndex] = NoteCarryingBend(channel with { Events = events }, noteBeforeBendIndex, restSplits, note, bend);
        }

        RefuseUnreadBendsBeforeFirstNote(channel, restSplits, bendsBeforeFirstNote);
        return channel with { Events = events, RestBends = [] };
    }

    // The note with the bend on its chain, and the rest behind it laid out as the bytes it is fetched as, up to the next note or the channel's end.
    private static MmlNote NoteCarryingBend(MmlChannel channel, int noteIndex, RestSplits restSplits, MmlNote note, MmlRestBend bend)
    {
        if (note.RestPieces.IsDefaultOrEmpty)
        {
            ImmutableArray<int> notePieces = note.Pieces.IsDefaultOrEmpty ? [.. MmlDurationEncoding.SplitRunIntoTiedPieces(note.LengthTicks)] : note.Pieces;
            note = note with
            {
                Pieces = notePieces,
                RestPieces = [.. MmlRemoteExpansion.RestPieces(channel, noteIndex, restSplits, note.Tick + notePieces.Sum(), NextNoteTick(channel, noteIndex))],
            };
        }

        return note.WithBendArmedAt(bend.Tick, bend.DelayTicks, bend.LengthTicks, bend.TargetNoteValue);
    }

    // Refuses the rest bends in front of a channel's first note that the driver does not read before their byte ends.
    private static void RefuseUnreadBendsBeforeFirstNote(MmlChannel channel, RestSplits restSplits, List<MmlRestBend> bends)
    {
        var bendsWithRamp = bends.Where(bend => bend.LengthTicks > MmlPitchBend.NoRampTicks).ToList();
        if (bendsWithRamp.Count == 0)
        {
            return;
        }

        var restPieces = MmlRemoteExpansion.RestPieces(channel, BeforeFirstEventIndex, restSplits, ChannelStartTick, NextNoteTick(channel, BeforeFirstEventIndex));
        var (_, firstUnreadBendIndex) = BendWalk.WalkChain(
            BendWalk.Layout.OfPieces(restPieces, NoNoteTicks),
            [.. bendsWithRamp.Select(bend => new BendWalk.WrittenRamp(bend.Tick, bend.DelayTicks, bend.LengthTicks))]);

        if (firstUnreadBendIndex is { } unread)
        {
            throw MmlImportException.UnreadBend(bendsWithRamp[unread].Line);
        }
    }

    // The tick of the first note after an index, or the channel's end.
    private static int NextNoteTick(MmlChannel channel, int eventIndex)
    {
        for (int laterIndex = eventIndex + 1; laterIndex < channel.Events.Count; laterIndex++)
        {
            if (channel.Events[laterIndex] is MmlNote nextNote)
            {
                return nextNote.Tick;
            }
        }

        return channel.EndTick;
    }
}
