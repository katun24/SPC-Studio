using System.Collections.Immutable;

namespace SpcStudio.Model;

// The song's channels of notes: placing, lifting for a drag and removing notes, each edit the note panel makes to one, and whether a channel sounds.
public sealed partial class Song
{
    private readonly List<List<Note>> _notes = [];

    // The notes taken out of their channels for a drag, which still hold the roll open.
    private readonly List<Note> _liftedNotes = [];

    // Whether no channel holds a note.
    public bool IsEmpty
    {
        get
        {
            return _notes.All(notes => notes.Count == 0);
        }
    }

    // Which channels are muted and soloed, and so which do not sound.
    public MuteAndSolo Muting { get; } = new(0);

    // A channel's notes, in time order and never overlapping.
    public IReadOnlyList<Note> Notes(int channel)
    {
        return _notes[channel];
    }

    // Every note of every channel with the channel it stands on, channel by channel and each channel's in time order.
    public IEnumerable<(int Channel, Note Note)> NotesWithChannels()
    {
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            foreach (var note in _notes[channel])
            {
                yield return (channel, note);
            }
        }
    }

    // Whether a channel holds no notes.
    public bool IsChannelEmpty(int channel)
    {
        return _notes[channel].Count == 0;
    }

    // The note sounding at a tick, or null where the channel is silent there.
    public Note? NoteAt(int channel, int tick)
    {
        return LaneUnits.UnitCovering(_notes[channel], tick);
    }

    // Adds a note, cutting whatever it overlaps back to the part outside it, and drops the lift.
    public void PlaceNote(int channel, Note note)
    {
        PlaceNotes([(channel, note)]);
    }

    // Adds a whole selection of notes, each onto the channel beside it, dropping the lift and following the roll once around the batch.
    public void PlaceNotes(IReadOnlyList<(int Channel, Note Note)> notes)
    {
        int previousRollTicks = RollLengthTicks;
        _liftedNotes.Clear();
        foreach (var (channel, note) in notes)
        {
            InsertNote(_notes[channel], note);
        }

        FollowRoll(previousRollTicks);
    }

    // A channel's notes as they would stand with a note placed among them, the channel itself left as it is.
    public IReadOnlyList<Note> NotesWithPlaced(int channel, Note note)
    {
        var notes = new List<Note>(_notes[channel]);
        InsertNote(notes, note);
        return notes;
    }

    // A note as it lands in a channel: its row brought into the noise band where it plays noise, anything its length cannot reach taken off its chains, and its instrument's values taken again.
    public Note NoteAsPlaced(Note note)
    {
        return SyncedWithInstrument(WithNoiseRowInBand(note).FittedToLength());
    }

    // Takes the note covering a tick out of its channel for a drag, still holding the roll open, and answers whether there was one.
    public bool LiftNote(int channel, int tick)
    {
        int noteIndex = NoteIndexCovering(channel, tick);
        if (noteIndex < 0)
        {
            return false;
        }

        _liftedNotes.Add(_notes[channel][noteIndex]);
        _notes[channel].RemoveAt(noteIndex);
        return true;
    }

    // The lifted notes as a drag has moved them, the roll and the markers following.
    public void SetLiftedNotes(IReadOnlyList<Note> notes)
    {
        int previousRollTicks = RollLengthTicks;
        ReplaceContents(_liftedNotes, notes);
        FollowRoll(previousRollTicks);
    }

    // Removes the note sounding at a tick, and answers whether there was one.
    public bool RemoveNoteAt(int channel, int tick)
    {
        int noteIndex = NoteIndexCovering(channel, tick);
        if (noteIndex < 0)
        {
            return false;
        }

        int previousRollTicks = RollLengthTicks;
        _notes[channel].RemoveAt(noteIndex);
        FollowRoll(previousRollTicks);
        return true;
    }

    // Replaces a channel's notes with ones already in time order and apart, each cut to what its length can reach.
    public void SetChannelNotes(int channel, IReadOnlyList<Note> notes)
    {
        ReplaceContents(_notes[channel], notes.Select(note => note.FittedToLength()));
    }

    // Points a note at another instrument.
    public Note? SetNoteInstrument(int channel, int startTick, int instrumentId)
    {
        return RewriteNote(channel, startTick, note => WithNoiseRowInBand(note with { InstrumentId = instrumentId }));
    }

    // Gives a note a q of its own, both halves.
    public Note? SetNoteQuantization(int channel, int startTick, int gate, int velocity)
    {
        return RewriteNote(channel, startTick, note => note with { Gate = gate, GateSource = NoteSource.Note, Velocity = velocity, VelocitySource = NoteSource.Note });
    }

    // Gives a note a q gate of its own.
    public Note? SetNoteGate(int channel, int startTick, int gate)
    {
        return RewriteNote(channel, startTick, note => NoteDefaults.WithQuantizationHalf(note, NoteDefault.Gate, gate));
    }

    // Gives a note a q velocity of its own.
    public Note? SetNoteVelocity(int channel, int startTick, int velocity)
    {
        return RewriteNote(channel, startTick, note => NoteDefaults.WithQuantizationHalf(note, NoteDefault.Velocity, velocity));
    }

    // Gives a note a fine tune of its own.
    public Note? SetNoteFineTune(int channel, int startTick, int fineTune)
    {
        return RewriteNote(channel, startTick, note => NoteDefaults.WithFineTune(note, fineTune));
    }

    // Moves one of a note's values onto its own rung or its instrument's, without changing what it plays.
    public Note? SetNoteSource(int channel, int startTick, NoteDefault value, NoteSource source)
    {
        return RewriteNote(channel, startTick, note => NoteDefaults.WithSource(note, value, source));
    }

    // Gives a note a legato flag of its own.
    public Note? SetNoteLegato(int channel, int startTick, bool isLegato)
    {
        return RewriteNote(channel, startTick, note => NoteDefaults.WithLegato(note, isLegato));
    }

    // Gives a note a hold flag of its own.
    public Note? SetNoteHold(int channel, int startTick, bool isHeld)
    {
        return RewriteNote(channel, startTick, note => NoteDefaults.WithHold(note, isHeld));
    }

    // Gives a note a pitch bend chain of its own, its level bend nodes going with the last bend.
    public Note? SetNoteBends(int channel, int startTick, ImmutableArray<PitchBend> bends)
    {
        return ReplaceNote(channel, startTick, note => WithOwnBendPath(note, note with { Bends = bends, FlatBendNodeTicks = bends.IsEmpty ? [] : note.FlatBendNodeTicks }));
    }

    // Gives a note the pitch path a bend node edit made of it - its written pitch, its chain and its level bend nodes - as its own.
    public Note? SetNoteBendPath(int channel, int startTick, Note reshaped)
    {
        return ReplaceNote(channel, startTick, note => WithOwnBendPath(note, reshaped));
    }

    // Replaces a note's pan rows and the rung it stands on, each row held to the pan's range and cut to what the note can reach.
    public Note? SetNotePan(int channel, int startTick, LaneSource source, ImmutableArray<NotePanRegion> pans)
    {
        return ReplaceNote(channel, startTick, note => note with
        {
            PanSource = source,
            Pans = OffsetRows.FitToNoteLength([.. pans.Select(NotePan.Lane.ClampedToRange)], note.LengthTicks),
        });
    }

    // Replaces a note's volume rows and the rung it stands on, each row held to the volume's range and cut to what the note can reach.
    public Note? SetNoteVolume(int channel, int startTick, LaneSource source, ImmutableArray<NoteVolumeRegion> volumes)
    {
        return RewriteNote(channel, startTick, note => note with
        {
            VolumeSource = source,
            Volumes = OffsetRows.FitToNoteLength([.. volumes.Select(NoteVolume.Lane.ClampedToRange)], note.LengthTicks),
        });
    }

    // Replaces a note's amplify and the rung it stands on, the amplify held to its range.
    public Note? SetNoteAmplify(int channel, int startTick, LaneSource source, int amplify)
    {
        return RewriteNote(channel, startTick, note => note with
        {
            AmplifySource = source,
            Amplify = SnesVolume.AmplifyRange.Clamp(amplify),
        });
    }

    // Replaces a note's echo send and the rung it stands on.
    public Note? SetNoteEcho(int channel, int startTick, LaneSource source, bool isEchoOn)
    {
        return RewriteNote(channel, startTick, note => note with { EchoSource = source, IsEchoOn = isEchoOn });
    }

    // Gives a note a vibrato chain of its own, its values held to their ranges and cut to what the note can reach.
    public Note? SetNoteVibratos(int channel, int startTick, ImmutableArray<NoteVibrato> vibratos)
    {
        return ReplaceNote(channel, startTick, note => note with
        {
            Vibratos = NoteSweep.FitToNoteLength(SnesVibrato.Sanitized(vibratos), note.LengthTicks),
            VibratoSource = NoteSource.Note,
        });
    }

    // Gives a note a tremolo chain of its own, its values held to their ranges and cut to what the note can reach.
    public Note? SetNoteTremolos(int channel, int startTick, ImmutableArray<NoteTremolo> tremolos)
    {
        return ReplaceNote(channel, startTick, note => note with
        {
            Tremolos = NoteSweep.FitToNoteLength(SnesTremolo.Sanitized(tremolos), note.LengthTicks),
            TremoloSource = NoteSource.Note,
        });
    }

    // Gives a note envelope rows of its own, cut to what the note can reach.
    public Note? SetNoteEnvelopes(int channel, int startTick, ImmutableArray<EnvelopeRow> envelopes)
    {
        return ReplaceNote(channel, startTick, note => note with
        {
            Envelopes = OffsetRows.FitToNoteLength(envelopes, note.LengthTicks),
            EnvelopeSource = NoteSource.Note,
        });
    }

    // Moves a note to another row.
    public Note? SetNoteSemitone(int channel, int startTick, int semitone)
    {
        return RewriteNote(channel, startTick, note => WithNoiseRowInBand(note with { Semitone = semitone }));
    }

    // Moves a note to another tick, as a drag does, trimming whatever it lands on.
    public Note? SetNoteStart(int channel, int startTick, int movedToTick)
    {
        return ReshapeNote(channel, startTick, note => note with { StartTick = movedToTick });
    }

    // Resizes a note from its start, as a drag does, trimming whatever it now runs into.
    public Note? SetNoteLength(int channel, int startTick, int lengthTicks)
    {
        return ReshapeNote(channel, startTick, note => note.WithSpan(note.StartTick, lengthTicks));
    }

    // Brings every note standing on an instrument's rung back in step with its instrument, and answers whether any moved.
    public bool SyncNotesWithInstruments()
    {
        bool isAnyNoteChanged = false;
        foreach (var notes in _notes)
        {
            for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
            {
                var syncedNote = SyncedWithInstrument(notes[noteIndex]);
                if (syncedNote == notes[noteIndex])
                {
                    continue;
                }

                notes[noteIndex] = syncedNote;
                isAnyNoteChanged = true;
            }
        }

        return isAnyNoteChanged;
    }

    // Inserts a note into a channel's notes, cutting it out of whatever it overlaps, every piece brought in step with its instrument.
    private void InsertNote(List<Note> notes, Note note)
    {
        LaneUnits.InsertCuttingOverlaps(notes, NoteAsPlaced(note), SyncedWithInstrument);
    }

    // Rewrites the note starting at a tick in place and brings it in step with its instrument, answering it as it now is or null where there is none.
    private Note? RewriteNote(int channel, int startTick, Func<Note, Note> rewrite)
    {
        return ReplaceNote(channel, startTick, note => SyncedWithInstrument(rewrite(note)));
    }

    // Takes the note starting at a tick out and places it again reshaped, as a drag does, trimming whatever it lands on, and answers it as it now is
    // or null where there is none.
    private Note? ReshapeNote(int channel, int startTick, Func<Note, Note> reshape)
    {
        int noteIndex = NoteIndexStartingAt(channel, startTick);
        if (noteIndex < 0)
        {
            return null;
        }

        var reshapedNote = reshape(_notes[channel][noteIndex]);
        LiftNote(channel, startTick);
        PlaceNote(channel, reshapedNote);
        return reshapedNote;
    }

    // Replaces the note starting at a tick in place, answering it as it now is or null where there is none.
    private Note? ReplaceNote(int channel, int startTick, Func<Note, Note> replace)
    {
        int noteIndex = NoteIndexStartingAt(channel, startTick);
        if (noteIndex < 0)
        {
            return null;
        }

        var replacedNote = replace(_notes[channel][noteIndex]);
        _notes[channel][noteIndex] = replacedNote;
        return replacedNote;
    }

    // Where in its channel the note starting at a tick stands, or -1.
    private int NoteIndexStartingAt(int channel, int startTick)
    {
        return LaneUnits.IndexStartingAt(_notes[channel], startTick);
    }

    // Where in its channel the note covering a tick stands, or -1.
    private int NoteIndexCovering(int channel, int tick)
    {
        return LaneUnits.IndexCovering(_notes[channel], tick);
    }

    // The note with whatever it takes from its instrument filled in from that instrument.
    private Note SyncedWithInstrument(Note note)
    {
        return NoteDefaults.SyncedWithInstrument(note, InstrumentOf(note));
    }

    // A note given another note's written pitch, chain and level bend nodes as its own, its row brought into the noise band where it is a noise note.
    private Note WithOwnBendPath(Note note, Note reshaped)
    {
        return WithNoiseRowInBand(note with
        {
            Semitone = reshaped.Semitone,
            Bends = PitchBend.FitToNoteLength(reshaped.Bends, note.LengthTicks),
            FlatBendNodeTicks = reshaped.FlatBendNodeTicks,
            BendSource = NoteSource.Note,
        });
    }

    // The note with its row brought into the noise band, where it is a noise note, and its own bends cut where the new row would carry them out of the roll.
    private Note WithNoiseRowInBand(Note note)
    {
        if (!IsNoiseNote(note) || SnesNoise.IsInBand(note.Semitone))
        {
            return note;
        }

        int bandSemitone = SnesNoise.ClampToBand(note.Semitone);
        var bends = note.BendSource == NoteSource.Note ? PitchBend.WithinRoll(note.Bends, bandSemitone) : note.Bends;
        return note with { Semitone = bandSemitone, Bends = bends };
    }
}
