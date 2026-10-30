using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's envelope: each note's envelope rows, read off the channel's envelope register writes.
public static partial class MmlImporter
{
    // Gives each of a channel's notes the envelope rows its writes make it play, where they leave the registers anywhere other than the instrument the notes carry would have put them.
    private static void ApplyEnvelope(Song song, int channel, List<EnvelopeRegisterWrite> commands)
    {
        if (!HasWriteOtherThanInstrument(commands))
        {
            return;
        }

        var notes = song.Notes(channel);

        // What each audible tick's writes leave, in tick order.
        var heardWrites = HeardEnvelopeWrites(notes, commands).OrderBy(heardWrite => heardWrite.AudibleTick).ToList();
        if (heardWrites.Count == 0)
        {
            return;
        }

        // The channel's envelope before any note holds rows of its own, which is the instrument timeline alone.
        var instrumentTimeline = EnvelopeMap.InstrumentTimelineOf(song, channel);

        // Every tick either the writes or the instrument timeline can move at, in order.
        var boundaryTicks = heardWrites.Select(heardWrite => heardWrite.AudibleTick).Concat(EnvelopeMap.InstrumentSegments(song, channel).Select(segment => segment.Tick)).Distinct().Order().ToList();

        // What the writes heard by a tick leave in the registers, the song load's own state before any.
        EnvelopeState RegisterStateAt(int tick)
        {
            int heardCount = SortedSearch.CountLeading(heardWrites, heardWrite => heardWrite.AudibleTick <= tick);
            return heardCount == 0 ? MmlBuiltins.DefaultEnvelope : heardWrites[heardCount - 1].LastWrite.After;
        }

        var noteList = notes.ToList();

        // The instrument timeline with a unit wherever what the writes leave in the registers differs from what it plays.
        var playedTimeline = instrumentTimeline.WithUnits(EnvelopeUnit.WherePlayedDiffers(boundaryTicks, song.EndTick, RegisterStateAt, instrumentTimeline));

        var rowsOfEach = playedTimeline.RowsOfEach(noteList);
        for (int noteIndex = 0; noteIndex < noteList.Count; noteIndex++)
        {
            song.SetNoteEnvelopes(channel, noteList[noteIndex].StartTick, rowsOfEach[noteIndex]);
        }
    }

    // Whether any of a channel's envelope writes is something other than an instrument change.
    private static bool HasWriteOtherThanInstrument(IEnumerable<EnvelopeRegisterWrite> commands)
    {
        return commands.Any(command => command.Wrote != EnvelopeWrite.Instrument);
    }

    // Where a write to the envelope registers can first be read: its own tick where a note of the channel is sounding there, and the channel's next key-on otherwise.
    private static int AudibleTick(IReadOnlyList<Note> notes, int tick)
    {
        if (LaneUnits.UnitCovering(notes, tick) is not null)
        {
            return tick;
        }

        int nextNoteIndex = LaneUnits.LastIndexStartingBy(notes, tick) + 1;
        return nextNoteIndex < notes.Count ? notes[nextNoteIndex].StartTick : tick;
    }

    // The writes grouped by the tick they can first be heard at, each group as the last write in it and whether an instrument change is among them, the
    // writes after the channel's last note, which no key-on ever reads, left out.
    private static IEnumerable<(int AudibleTick, EnvelopeRegisterWrite LastWrite, bool HasInstrumentChange)>
        HeardEnvelopeWrites(IReadOnlyList<Note> notes, List<EnvelopeRegisterWrite> commands)
    {
        return commands
            .GroupBy(command => AudibleTick(notes, command.Tick))
            .Where(group => LaneUnits.UnitCovering(notes, group.Key) is not null)
            .Select(group => (
                AudibleTick: group.Key,
                LastWrite: group.Last(),
                HasInstrumentChange: group.Any(command => command.Wrote is EnvelopeWrite.Instrument or EnvelopeWrite.SampleLoad)));
    }

    // The last of the notes to key on at or before a tick, or null where none has.
    private static Note? LastNoteStartedBy(IReadOnlyList<Note> notes, int tick)
    {
        int noteIndex = LaneUnits.LastIndexStartingBy(notes, tick);
        return noteIndex >= 0 ? notes[noteIndex] : null;
    }

    // Refuses a channel whose envelope writes leave the registers anywhere other than where its notes and units play them.
    private static void VerifyEnvelope(Song song, int channel, List<EnvelopeRegisterWrite> commands)
    {
        if (!HasWriteOtherThanInstrument(commands))
        {
            return;
        }

        var envelopeTimeline = EnvelopeMap.For(song, channel);
        var notes = song.Notes(channel);
        foreach (var (audibleTick, lastWrite, _) in HeardEnvelopeWrites(notes, commands))
        {
            var playedState = envelopeTimeline.StateAt(audibleTick);
            if (playedState == lastWrite.After)
            {
                continue;
            }

            throw MmlImportException.EnvelopeLeftUnplayed(lastWrite.Wrote, lastWrite.Tick, channel, audibleTick, lastWrite.After, playedState, lastWrite.Line);
        }
    }

    // Which command a point in a channel's envelope walk was.
    internal enum EnvelopeWrite
    {
        Instrument,
        SampleLoad,
        Override,
        Gain,
        DspWrite,
        RemoteGain,
        Restore,
        KeyOnRestore,
    }

    // One point at which a channel's envelope registers change: the registers before and after, which command it was, and the instrument in force after it.
    private readonly record struct EnvelopeRegisterWrite(int Tick, EnvelopeState Before, EnvelopeState After, EnvelopeWrite Wrote, int Instrument, int Line);
}
