using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Import;

// Gives each instrument of an imported song the values its notes share, the notes playing one then taking it from the instrument: the q's two halves,
// the fine tune, the legato and hold flags, the bend, vibrato and tremolo chains, the amplify and the envelope, and on a transcription the channel volume too.
public static class SharedNoteValues
{
    // Moves every value more than half of each instrument's notes play onto the instrument.
    public static void MoveMajorityValuesOntoInstruments(Song song)
    {
        MoveValuesOntoInstruments(song, MajorityFloorCount);
    }

    // Moves every value each instrument's notes play most often onto the instrument however few of them play it, the channel volume included.
    public static void MoveMostPlayedValuesOntoInstruments(Song song)
    {
        foreach (var (instrument, notes) in MoveValuesOntoInstruments(song, MostPlayedFloorCount))
        {
            MoveSharedVolume(song, instrument, notes, MostPlayedFloorCount(notes.Count));
        }
    }

    // Moves every value each instrument's notes share past a floor onto the instrument, and returns every instrument that plays a note with its notes.
    private static List<(Instrument Instrument, List<(int Channel, Note Note)> Notes)> MoveValuesOntoInstruments(Song song, Func<int, int> floorCountOf)
    {
        RunTailsToNoteEnds(song);
        var instrumentsWithNotes = InstrumentsWithNotes(song);
        foreach (var (instrument, notes) in instrumentsWithNotes)
        {
            MoveSharedNoteDefaults(song, instrument, notes, floorCountOf(notes.Count));
            MoveSharedAmplify(song, instrument, notes, floorCountOf(notes.Count));
        }

        return instrumentsWithNotes;
    }

    // Has the last stretch of every note's vibrato, tremolo and pitch bend chain run on to the note's end wherever it already ends exactly there.
    private static void RunTailsToNoteEnds(Song song)
    {
        for (int channel = 0; channel < song.ChannelCount; channel++)
        {
            song.SetChannelNotes(channel, [.. song.Notes(channel).Select(note => note.WithSweepTailsRunToEnd())]);
        }
    }

    // How many of an instrument's notes a value must be played by more than to move onto it, as more than half.
    private static int MajorityFloorCount(int noteCount)
    {
        return noteCount / 2;
    }

    // How many of an instrument's notes a value must be played by more than to move onto it, as any at all.
    private static int MostPlayedFloorCount(int noteCount)
    {
        return 0;
    }

    // Every instrument of the song that plays a note, in the song's order, each with its notes and their channels.
    private static List<(Instrument Instrument, List<(int Channel, Note Note)> Notes)> InstrumentsWithNotes(Song song)
    {
        var notesByInstrument = new Dictionary<int, List<(int Channel, Note Note)>>();
        for (int channel = 0; channel < song.ChannelCount; channel++)
        {
            foreach (var note in song.Notes(channel))
            {
                if (song.InstrumentOf(note) is null)
                {
                    continue;
                }

                if (!notesByInstrument.TryGetValue(note.InstrumentId, out var notes))
                {
                    notes = [];
                    notesByInstrument[note.InstrumentId] = notes;
                }

                notes.Add((channel, note));
            }
        }

        return [.. song.Instruments.Where(instrument => notesByInstrument.ContainsKey(instrument.Id)).Select(instrument => (instrument, notesByInstrument[instrument.Id]))];
    }

    // Gives the instrument each value the most of its notes would play from it, past a floor, then puts every note that plays the same from it onto its rung.
    private static void MoveSharedNoteDefaults(Song song, Instrument instrument, IReadOnlyList<(int Channel, Note Note)> notes, int floorCount)
    {
        var sharedValues = new List<NoteDefault>();
        foreach (var value in Enum.GetValues<NoteDefault>())
        {
            if (MostSharedNote(instrument, notes, value, floorCount) is not { } sharingNote)
            {
                continue;
            }

            NoteDefaults.CopyOntoInstrument(instrument, sharingNote, value);
            sharedValues.Add(value);
        }

        foreach (var (channel, note) in notes)
        {
            foreach (var value in sharedValues)
            {
                if (NoteDefaults.PlaysSameFromInstrument(note, value, instrument))
                {
                    song.SetNoteSource(channel, note.StartTick, value, NoteSource.Instrument);
                }
            }
        }
    }

    // The note whose value the most of the instrument's notes would play unchanged from the instrument, where that is more of them than a floor, or null.
    private static Note? MostSharedNote(Instrument instrument, IReadOnlyList<(int Channel, Note Note)> notes, NoteDefault value, int floorCount)
    {
        var trialInstrument = instrument.Clone();
        var oneNotePerValueMostHeldFirst = MostHeld.DistinctByCount(notes.Select(channelNote => channelNote.Note), (first, second) => NoteDefaults.HasSameValue(first, second, value))
            .Select(distinctValue => distinctValue.Item);
        return MostShared(oneNotePerValueMostHeldFirst, notes.Count, floorCount, candidateNote =>
        {
            NoteDefaults.CopyOntoInstrument(trialInstrument, candidateNote, value);
            return noteIndex => NoteDefaults.PlaysSameFromInstrument(notes[noteIndex].Note, value, trialInstrument);
        });
    }

    // The candidate the most entries share, where that is more of them than a floor, or null: tried in the order given, a later one replacing the best
    // only by beating it, each readied once and then asked whether each entry shares it.
    private static T? MostShared<T>(IEnumerable<T> candidates, int entryCount, int floorCount, Func<T, Func<int, bool>> sharingTestFor) where T : struct
    {
        T? bestCandidate = null;
        int bestSharingCount = floorCount;
        foreach (var candidate in candidates)
        {
            int sharingCount = SharingCountAbove(entryCount, bestSharingCount, sharingTestFor(candidate));
            if (sharingCount > bestSharingCount)
            {
                bestSharingCount = sharingCount;
                bestCandidate = candidate;
            }
        }

        return bestCandidate;
    }

    // How many of the entries share a candidate, counting stopped once the count can no longer pass the floor.
    private static int SharingCountAbove(int entryCount, int floorCount, Func<int, bool> doesEntryShare)
    {
        int sharingCount = 0;
        for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            if (sharingCount + entryCount - entryIndex <= floorCount)
            {
                break;
            }

            if (doesEntryShare(entryIndex))
            {
                sharingCount++;
            }
        }

        return sharingCount;
    }

    // Gives the instrument the amplify the most of its notes play throughout, past a floor, then puts every note playing it throughout onto the instrument's rung.
    private static void MoveSharedAmplify(Song song, Instrument instrument, IReadOnlyList<(int Channel, Note Note)> notes, int floorCount)
    {
        var noteAmplifiesHeldThroughout = notes.Select(channelNote => NoteAmplify.Lane.ValueHeldThroughout(song, channelNote.Channel, channelNote.Note)).ToList();
        if (MostHeldOf(noteAmplifiesHeldThroughout, floorCount) is not { } sharedAmplify)
        {
            return;
        }

        instrument.Amplify = sharedAmplify;
        for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
        {
            if (noteAmplifiesHeldThroughout[noteIndex] == sharedAmplify)
            {
                NoteAmplify.Lane.SetSource(song, notes[noteIndex].Channel, notes[noteIndex].Note, LaneSource.Instrument);
            }
        }
    }

    // Gives the instrument the channel volume shape the most of its notes play over the lane, past a floor, then puts every note playing it onto the instrument's rung.
    private static void MoveSharedVolume(Song song, Instrument instrument, IReadOnlyList<(int Channel, Note Note)> notes, int floorCount)
    {
        var playedShapes = LaneVolumeShapes(song, notes);

        // Each distinct volume shape the notes play over the lane, the most played first.
        var candidateShapes = MostHeld.DistinctByCount(playedShapes.OfType<ImmutableArray<NoteChainRegion<int>>>(), (first, second) => first.SequenceEqual(second))
            .Select(distinctShape => distinctShape.Item);
        var sharedShape = MostShared(candidateShapes, notes.Count, floorCount, candidateShape => noteIndex => PlaysVolumeShape(playedShapes[noteIndex], candidateShape, notes[noteIndex].Note));
        if (sharedShape is not { } shape)
        {
            return;
        }

        NoteVolume.Lane.SetInstrumentRows(instrument, shape);
        for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
        {
            if (PlaysVolumeShape(playedShapes[noteIndex], shape, notes[noteIndex].Note))
            {
                NoteVolume.Lane.SetSource(song, notes[noteIndex].Channel, notes[noteIndex].Note, LaneSource.Instrument);
            }
        }
    }

    // The channel volume each note plays over the lane alone, as a shape counted from its start, or none where it holds a volume of its own or the lane ramps across it.
    private static List<ImmutableArray<NoteChainRegion<int>>?> LaneVolumeShapes(Song song, IReadOnlyList<(int Channel, Note Note)> notes)
    {
        var shapes = new List<ImmutableArray<NoteChainRegion<int>>?>(new ImmutableArray<NoteChainRegion<int>>?[notes.Count]);
        foreach (var channelEntries in notes.Select((channelNote, noteIndex) => (channelNote, noteIndex)).GroupBy(entry => entry.channelNote.Channel))
        {
            var entries = channelEntries.ToList();
            var laneShapes = NoteVolume.Lane.LaneStepsAcross(song, channelEntries.Key, [.. entries.Select(entry => entry.channelNote.Note)]);
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                bool isOnLane = entries[entryIndex].channelNote.Note.VolumeSource == LaneSource.None;
                shapes[entries[entryIndex].noteIndex] = isOnLane ? laneShapes[entryIndex] : null;
            }
        }

        return shapes;
    }

    // Whether a note playing a volume shape plays the same from an instrument holding a whole shape, which it plays cut to its own length.
    private static bool PlaysVolumeShape(ImmutableArray<NoteChainRegion<int>>? playedShape, ImmutableArray<NoteChainRegion<int>> instrumentShape, Note note)
    {
        return playedShape is { } shape && shape.SequenceEqual(OffsetRows.FitToNoteLength(instrumentShape, note.LengthTicks));
    }

    // The value the most entries hold, where that is more of them than a floor, an empty entry holding none, or null where no value does.
    private static T? MostHeldOf<T>(IReadOnlyList<T?> values, int floorCount) where T : struct
    {
        var distinctValuesMostHeldFirst = MostHeld.DistinctByCount(values.OfType<T>(), (first, second) => first.Equals(second)).Select(distinctValue => distinctValue.Item);
        return MostShared(distinctValuesMostHeldFirst, values.Count, floorCount, candidateValue => valueIndex => values[valueIndex] is { } value && value.Equals(candidateValue));
    }
}
