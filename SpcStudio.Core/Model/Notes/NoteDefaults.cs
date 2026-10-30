using System.Collections.Immutable;

namespace SpcStudio.Model;

// The instrument defaults for the values a note carries with no lane beneath them: the q gate and velocity, the $EE fine tune, the p, $E5 and $DD
// chains, the $ED envelope rows, and the $F4 $01 legato and $FC hold flags.
public static class NoteDefaults
{
    // The note with every value on the instrument's rung set to the instrument's, and the q kept off q00.
    public static Note SyncedWithInstrument(Note note, Instrument? instrument)
    {
        if (instrument is null)
        {
            return note;
        }

        var halfKeptOnSync = note.GateSource == NoteSource.Instrument ? NoteDefault.Gate : NoteDefault.Velocity;
        var (gate, velocity) = Note.QuantizationKeeping(
            halfKeptOnSync,
            note.GateSource == NoteSource.Instrument ? instrument.Gate : note.Gate,
            note.VelocitySource == NoteSource.Instrument ? instrument.Velocity : note.Velocity);
        int fineTune = note.FineTuneSource == NoteSource.Instrument ? instrument.FineTune : note.FineTune;

        bool isLegato = note.LegatoSource == NoteSource.Instrument ? instrument.IsLegato : note.IsLegato;
        bool isHeld = note.HoldSource == NoteSource.Instrument ? instrument.IsHeld : note.IsHeld;
        var playedVibratos = note.VibratoSource == NoteSource.Instrument ? NoteSweep.FitToNoteLength(instrument.Vibratos, note.LengthTicks) : note.Vibratos;
        var playedTremolos = note.TremoloSource == NoteSource.Instrument ? NoteSweep.FitToNoteLength(instrument.Tremolos, note.LengthTicks) : note.Tremolos;
        var playedEnvelopes = note.EnvelopeSource == NoteSource.Instrument
            ? OffsetRows.FitToNoteLength(EnvelopeRows.OfInstrument(instrument), note.LengthTicks)
            : EnvelopeRows.PlayedBy(note, instrument);
        var playedBends = note.BendSource == NoteSource.Instrument ? PitchBend.Carried(instrument.Bends, note.Semitone, note.LengthTicks, isHeld) : note.Bends;
        bool isAlreadySynced = gate == note.Gate
            && velocity == note.Velocity
            && fineTune == note.FineTune
            && isLegato == note.IsLegato
            && isHeld == note.IsHeld
            && playedVibratos.SequenceEqual(note.Vibratos)
            && playedTremolos.SequenceEqual(note.Tremolos)
            && playedEnvelopes.SequenceEqual(note.Envelopes)
            && playedBends.SequenceEqual(note.Bends);
        if (isAlreadySynced)
        {
            return note;
        }

        return note with
        {
            Gate = gate,
            Velocity = velocity,
            FineTune = fineTune,
            IsLegato = isLegato,
            IsHeld = isHeld,
            Vibratos = playedVibratos,
            Tremolos = playedTremolos,
            Envelopes = playedEnvelopes,
            Bends = playedBends,
        };
    }

    // The note standing on its instrument's rung for every value it can take from it but the echo send.
    public static Note OnInstrumentRungsExceptEcho(Note note)
    {
        return note with
        {
            PanSource = LaneSource.Instrument,
            VolumeSource = LaneSource.Instrument,
            AmplifySource = LaneSource.Instrument,
            GateSource = NoteSource.Instrument,
            VelocitySource = NoteSource.Instrument,
            FineTuneSource = NoteSource.Instrument,
            VibratoSource = NoteSource.Instrument,
            TremoloSource = NoteSource.Instrument,
            BendSource = NoteSource.Instrument,
            LegatoSource = NoteSource.Instrument,
            HoldSource = NoteSource.Instrument,
            EnvelopeSource = NoteSource.Instrument,
        };
    }

    // The note given one half of its q of its own, the other half bumped off 0 where both would be 0 and then taken onto the note's own rung.
    public static Note WithQuantizationHalf(Note note, NoteDefault half, int value)
    {
        var (gate, velocity) = Note.QuantizationWithHalfSet(half, value, note.Gate, note.Velocity);

        // The rung a half stands on after the edit: the note's own where it was set or bumped, and the one it stood on otherwise.
        NoteSource SourceAfterEdit(NoteDefault ownHalf, int editedValue, int previousValue, NoteSource previousSource)
        {
            return half == ownHalf || editedValue != previousValue ? NoteSource.Note : previousSource;
        }

        return note with
        {
            Gate = gate,
            GateSource = SourceAfterEdit(NoteDefault.Gate, gate, note.Gate, note.GateSource),
            Velocity = velocity,
            VelocitySource = SourceAfterEdit(NoteDefault.Velocity, velocity, note.Velocity, note.VelocitySource),
        };
    }

    // The note given a fine tune of its own.
    public static Note WithFineTune(Note note, int fineTune)
    {
        return note with { FineTune = fineTune, FineTuneSource = NoteSource.Note };
    }

    // The note given a legato flag of its own.
    public static Note WithLegato(Note note, bool isLegato)
    {
        return note with { IsLegato = isLegato, LegatoSource = NoteSource.Note };
    }

    // The note given a hold flag of its own.
    public static Note WithHold(Note note, bool isHeld)
    {
        return note with { IsHeld = isHeld, HoldSource = NoteSource.Note };
    }

    // Which rung one of the note's values stands on.
    public static NoteSource SourceOf(Note note, NoteDefault value)
    {
        return value switch
        {
            NoteDefault.Gate => note.GateSource,
            NoteDefault.Velocity => note.VelocitySource,
            NoteDefault.FineTune => note.FineTuneSource,
            NoteDefault.Vibrato => note.VibratoSource,
            NoteDefault.Tremolo => note.TremoloSource,
            NoteDefault.Legato => note.LegatoSource,
            NoteDefault.Hold => note.HoldSource,
            NoteDefault.Envelope => note.EnvelopeSource,
            _ => note.BendSource,
        };
    }

    // The note moved onto a rung for one value, keeping what it plays.
    public static Note WithSource(Note note, NoteDefault value, NoteSource source)
    {
        return value switch
        {
            NoteDefault.Gate => note with { GateSource = source },
            NoteDefault.Velocity => note with { VelocitySource = source },
            NoteDefault.FineTune => note with { FineTuneSource = source },
            NoteDefault.Vibrato => note with { VibratoSource = source },
            NoteDefault.Tremolo => note with { TremoloSource = source },
            NoteDefault.Legato => note with { LegatoSource = source },
            NoteDefault.Hold => note with { HoldSource = source },
            NoteDefault.Envelope => note with { EnvelopeSource = source },
            _ => note with { BendSource = source },
        };
    }

    // Whether two notes play the same for one value, a chain compared element by element.
    public static bool HasSameValue(Note first, Note second, NoteDefault value)
    {
        return value switch
        {
            NoteDefault.Gate => first.Gate == second.Gate,
            NoteDefault.Velocity => first.Velocity == second.Velocity,
            NoteDefault.FineTune => first.FineTune == second.FineTune,
            NoteDefault.Vibrato => first.Vibratos.SequenceEqual(second.Vibratos),
            NoteDefault.Tremolo => first.Tremolos.SequenceEqual(second.Tremolos),
            NoteDefault.Legato => first.IsLegato == second.IsLegato,
            NoteDefault.Hold => first.IsHeld == second.IsHeld,
            NoteDefault.Envelope => first.Envelopes.SequenceEqual(second.Envelopes),
            _ => first.Bends.SequenceEqual(second.Bends),
        };
    }

    // Gives the instrument the note's value for one value, through the setter that value's instrument field takes.
    public static void CopyOntoInstrument(Instrument instrument, Note note, NoteDefault value)
    {
        switch (value)
        {
            case NoteDefault.Gate:
            case NoteDefault.Velocity:
                SetQuantizationHalf(instrument, value, PlayedValueOf(note, value));
                break;
            case NoteDefault.FineTune:
                instrument.FineTune = note.FineTune;
                break;
            case NoteDefault.Vibrato:
                SetVibratos(instrument, note.Vibratos);
                break;
            case NoteDefault.Tremolo:
                SetTremolos(instrument, note.Tremolos);
                break;
            case NoteDefault.Legato:
                instrument.IsLegato = note.IsLegato;
                break;
            case NoteDefault.Hold:
                instrument.IsHeld = note.IsHeld;
                break;
            case NoteDefault.Envelope:
                SetEnvelopes(instrument, note.Envelopes);
                break;
            default:
                SetBends(instrument, note.Bends);
                break;
        }
    }

    // Whether the note would play exactly what it plays now with one value taken from the instrument instead, a last stretch ending exactly at the
    // note's end being the same whether it runs on to the end or not.
    public static bool PlaysSameFromInstrument(Note note, NoteDefault value, Instrument instrument)
    {
        var movedNote = WithSource(note, value, NoteSource.Instrument);
        return SyncedWithInstrument(movedNote, instrument).WithSweepTailsRunToEnd() == movedNote.WithSweepTailsRunToEnd();
    }

    // The note's value for one of the three single numbers, whichever rung it stands on.
    public static int PlayedValueOf(Note note, NoteDefault value)
    {
        return value switch
        {
            NoteDefault.Gate => note.Gate,
            NoteDefault.Velocity => note.Velocity,
            NoteDefault.FineTune => note.FineTune,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    // The instrument's own value for one of the three single numbers, which is all a chain or a flag cannot answer.
    public static int InstrumentValueOf(Instrument instrument, NoteDefault value)
    {
        return value switch
        {
            NoteDefault.Gate => instrument.Gate,
            NoteDefault.Velocity => instrument.Velocity,
            NoteDefault.FineTune => instrument.FineTune,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    // Sets one half of the instrument's q, the other bumped off 0 where both would be 0.
    public static void SetQuantizationHalf(Instrument instrument, NoteDefault half, int value)
    {
        (instrument.Gate, instrument.Velocity) = Note.QuantizationWithHalfSet(half, value, instrument.Gate, instrument.Velocity);
    }

    // Sets the instrument's vibrato, held to the ranges and the fade rules a note's own is.
    public static void SetVibratos(Instrument instrument, ImmutableArray<NoteVibrato> vibratos)
    {
        instrument.Vibratos = SnesVibrato.Sanitized(vibratos);
    }

    // Sets the instrument's tremolo, held to the ranges a note's own is.
    public static void SetTremolos(Instrument instrument, ImmutableArray<NoteTremolo> tremolos)
    {
        instrument.Tremolos = SnesTremolo.Sanitized(tremolos);
    }

    // Sets the instrument's pitch bend chain, held to the ranges a bend takes and to the width of the roll.
    public static void SetBends(Instrument instrument, ImmutableArray<PitchBend> bends)
    {
        instrument.Bends = PitchBend.Sanitized(bends);
    }

    // Sets the instrument's envelope rows, the first becoming its own three bytes and the rest the changes after its key-on.
    public static void SetEnvelopes(Instrument instrument, ImmutableArray<EnvelopeRow> envelopes)
    {
        instrument.Envelope = envelopes[0].State;
        instrument.EnvelopeChanges = [.. envelopes.Skip(1)];
    }
}
