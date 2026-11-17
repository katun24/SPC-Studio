using SpcStudio.Model;

namespace SpcStudio.Import;

internal static partial class MmlRemoteExpansion
{
    // Every channel with each note whose own cuts go unmade and that a rest follows rung on through the rest, the song's rests skipping the key-off they make
    // when they are read under a hot patch - the notes a key-off arm reaches being left to that arm's expansion.
    public static IReadOnlyList<MmlChannel> RingThroughRestsUnderPatch(MmlFile file, MmlIgnored skippingPatch)
    {
        var waitTimeAt = WaitTimeLookup(file);
        return [.. file.Channels.Select(channel => channel with { Events = EventsRungThroughRests(channel, skippingPatch, waitTimeAt) })];
    }

    // A channel's events with each note outside a key-off arm rung on through the rest after it where its own cuts go unmade.
    private static List<MmlEvent> EventsRungThroughRests(MmlChannel channel, MmlIgnored skippingPatch, Func<int, int> waitTimeAt)
    {
        var restSplits = RestSplits.ForChannel(channel);
        var events = channel.Events.ToList();
        var isLegatoBitSetAt = LegatoBits.IsBitSetAtEach(events);
        var keyOffArming = default(KeyOffArming);
        for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            switch (events[eventIndex])
            {
                case MmlRemoteCall call:
                    keyOffArming = keyOffArming.After(call);
                    break;

                case MmlNote note when !ArmingAtNoteEnd(channel, eventIndex, note, keyOffArming).IsArmed:
                    events[eventIndex] = NoteRungThroughRest(channel, eventIndex, note, isLegatoBitSetAt[eventIndex], skippingPatch, waitTimeAt, restSplits);
                    break;
            }
        }

        return events;
    }

    // The key-off arming a note's last byte is fetched under: what was armed when it keyed on, moved by every call written inside it.
    private static KeyOffArming ArmingAtNoteEnd(MmlChannel channel, int noteIndex, MmlNote note, KeyOffArming armingAtKeyOn)
    {
        var arming = armingAtKeyOn;
        for (int eventIndex = noteIndex + 1; eventIndex < channel.Events.Count && channel.Events[eventIndex].Tick < note.EndTick; eventIndex++)
        {
            if (channel.Events[eventIndex] is MmlRemoteCall call)
            {
                arming = arming.After(call);
            }
        }

        return arming;
    }

    // A note rung on through the rest after it where its own cuts go unmade - under the legato, past a loop edge whose read-ahead finds a tie, or in a last
    // byte too short to reach either - and the note as written otherwise.
    private static MmlNote NoteRungThroughRest(
        MmlChannel channel, int noteIndex, MmlNote note, bool isLegatoBitSet, MmlIgnored skippingPatch, Func<int, int> waitTimeAt, RestSplits restSplits)
    {
        int noteEndTick = note.EndTick;
        if (FollowingRestTicks(channel, noteIndex, noteEndTick) is null)
        {
            return note;
        }

        var legatoBits = LegatoBits.AtCutsOf(channel.Events, noteIndex, noteEndTick, isLegatoBitSet);
        var noteKeyOffTriggers = KeyOffTriggersOf(channel, noteIndex, note, waitTimeAt);
        if (!legatoBits.DoesAbandonCuts && !IsHeldPastEnd(ReadAheadStopAfterNote(channel, noteIndex, noteEndTick, restSplits), noteKeyOffTriggers))
        {
            return note;
        }

        bool isAddMusicFour = skippingPatch.IsAddMusicFourPreset;
        var (stopTick, ringEnd) = RingThroughRest(
            channel, noteIndex, noteEndTick, Note.GateOfQuantization(note.Quantization), legatoBits, default, false, waitTimeAt, restSplits,
            tieLine => isAddMusicFour ? MmlImportException.TieAtLoopEdgeInAddMusicFourSong(tieLine) : MmlImportException.TieAtLoopEdgeUnderHotPatch(tieLine));
        return NoteSoundingTo(
            channel, noteIndex, note, stopTick, ringEnd, legatoBits.DoesAbandonCuts, waitTimeAt,
            () => isAddMusicFour
                ? MmlImportException.LegatoAtChannelEndInAddMusicFourSong(channel.Number, note.Tick, note.Line)
                : MmlImportException.LegatoAtChannelEndUnderHotPatch(channel.Number, note.Tick, note.Line));
    }
}
