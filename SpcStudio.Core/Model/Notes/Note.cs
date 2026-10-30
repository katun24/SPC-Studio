using System.Collections.Immutable;

namespace SpcStudio.Model;

// One note on one channel: where it starts and how long it lasts in ticks, its pitch as semitones above C4, the instrument it plays, the two halves of the q it plays under, and everything else a note
// carries of its own - its sweeps, bends, volume and pan chains, envelope rows, amplify, echo send and flags.
public readonly record struct Note(
    int StartTick,
    int LengthTicks,
    int Semitone,
    int InstrumentId,
    int Gate = SnesTiming.DefaultGate,
    int Velocity = SnesVolume.DefaultVelocity,
    int FineTune = SnesPitch.MinFineTune,
    bool IsLegato = false,
    bool IsHeld = false) : ILaneUnit<Note>
{
    public const int MinQuantization = 0x01;
    private const int QuantizationGateShift = 4;
    private const int QuantizationVelocityMask = 0x0F;

    private readonly ImmutableArray<PitchBend> _bends = ImmutableArray<PitchBend>.Empty;
    private readonly ImmutableArray<int> _flatBendNodeTicks = ImmutableArray<int>.Empty;
    private readonly ImmutableArray<NoteVibrato> _vibratos = ImmutableArray<NoteVibrato>.Empty;
    private readonly ImmutableArray<NoteTremolo> _tremolos = ImmutableArray<NoteTremolo>.Empty;
    private readonly ImmutableArray<NotePanRegion> _pans = ImmutableArray<NotePanRegion>.Empty;
    private readonly ImmutableArray<NoteVolumeRegion> _volumes = ImmutableArray<NoteVolumeRegion>.Empty;
    private readonly ImmutableArray<EnvelopeRow> _envelopes = ImmutableArray<EnvelopeRow>.Empty;

    // The note's $DD chain in the order the driver consumes it, each bend's delay counted from where the one before it ended, never holding a bend that moves no pitch, and never a default array.
    public ImmutableArray<PitchBend> Bends
    {
        get
        {
            return _bends.IsDefault ? ImmutableArray<PitchBend>.Empty : _bends;
        }

        init
        {
            _bends = value.IsDefault ? ImmutableArray<PitchBend>.Empty : PitchBend.WithoutLevelBends(value);
        }
    }

    // The ticks from the note's start of the bend nodes that stand where the path is level on both sides, which the chain says nothing of, in time order and never a default array.
    public ImmutableArray<int> FlatBendNodeTicks
    {
        get
        {
            return _flatBendNodeTicks.IsDefault ? ImmutableArray<int>.Empty : _flatBendNodeTicks;
        }

        init
        {
            _flatBendNodeTicks = value.IsDefault ? ImmutableArray<int>.Empty : value;
        }
    }

    // The note's p stretches in order, the first the vibrato it keys on under where it starts at the note's start, and never a default array.
    public ImmutableArray<NoteVibrato> Vibratos
    {
        get
        {
            return _vibratos.IsDefault ? ImmutableArray<NoteVibrato>.Empty : _vibratos;
        }

        init
        {
            _vibratos = value.IsDefault ? ImmutableArray<NoteVibrato>.Empty : value;
        }
    }

    // The note's $E5 stretches in order, the vibrato's shape one command over, and never a default array.
    public ImmutableArray<NoteTremolo> Tremolos
    {
        get
        {
            return _tremolos.IsDefault ? ImmutableArray<NoteTremolo>.Empty : _tremolos;
        }

        init
        {
            _tremolos = value.IsDefault ? ImmutableArray<NoteTremolo>.Empty : value;
        }
    }

    // The note's own pan chain, MML y, read only while the note stands on its own rung, and never a default array.
    public ImmutableArray<NotePanRegion> Pans
    {
        get
        {
            return _pans.IsDefault ? ImmutableArray<NotePanRegion>.Empty : _pans;
        }

        init
        {
            _pans = value.IsDefault ? ImmutableArray<NotePanRegion>.Empty : value;
        }
    }

    public LaneSource PanSource { get; init; }

    // The note's own channel volume chain, MML v, read only while the note stands on its own rung, and never a default array.
    public ImmutableArray<NoteVolumeRegion> Volumes
    {
        get
        {
            return _volumes.IsDefault ? ImmutableArray<NoteVolumeRegion>.Empty : _volumes;
        }

        init
        {
            _volumes = value.IsDefault ? ImmutableArray<NoteVolumeRegion>.Empty : value;
        }
    }

    public LaneSource VolumeSource { get; init; }

    // The note's envelope rows in order, the first the envelope it keys on under, and never a default array.
    public ImmutableArray<EnvelopeRow> Envelopes
    {
        get
        {
            return _envelopes.IsDefault ? ImmutableArray<EnvelopeRow>.Empty : _envelopes;
        }

        init
        {
            _envelopes = value.IsDefault ? ImmutableArray<EnvelopeRow>.Empty : value;
        }
    }

    public int Amplify { get; init; }
    public LaneSource AmplifySource { get; init; }
    public bool IsEchoOn { get; init; }
    public LaneSource EchoSource { get; init; }
    public NoteSource GateSource { get; init; }
    public NoteSource VelocitySource { get; init; }
    public NoteSource FineTuneSource { get; init; }
    public NoteSource VibratoSource { get; init; }
    public NoteSource TremoloSource { get; init; }
    public NoteSource BendSource { get; init; }
    public NoteSource EnvelopeSource { get; init; }
    public NoteSource LegatoSource { get; init; }
    public NoteSource HoldSource { get; init; }

    // The vibrato the note keys on under: its stretch at the note's start, or off where it has none.
    public VibratoState KeyOnVibrato
    {
        get
        {
            return Vibratos is [var first, ..] && first.DelayTicks == NoteSweep.MinDelayTicks ? first.State : VibratoState.Off;
        }
    }

    // The tremolo the note keys on under: its stretch at the note's start, or off where it has none.
    public TremoloState KeyOnTremolo
    {
        get
        {
            return Tremolos is [var first, ..] && first.DelayTicks == NoteSweep.MinDelayTicks ? first.State : TremoloState.Off;
        }
    }

    // Ticks from the note's start to the end of its last vibrato stretch, where one added to the chain would begin counting its own delay from.
    public int VibratoChainTicks
    {
        get
        {
            return Vibratos.Sum(vibrato => NoteSweep.SpanTicks(vibrato));
        }
    }

    // Ticks from the note's start to the end of its last tremolo stretch, where one added to the chain would begin counting its own delay from.
    public int TremoloChainTicks
    {
        get
        {
            return Tremolos.Sum(tremolo => NoteSweep.SpanTicks(tremolo));
        }
    }

    // Ticks from the note's start to the end of its last bend's ramp, where a bend added to the chain would begin counting its own delay from.
    public int BendChainTicks
    {
        get
        {
            return Bends.Sum(bend => NoteSweep.SpanTicks(bend));
        }
    }

    // Whether the note bends its pitch at all.
    public bool HasBend
    {
        get
        {
            return !Bends.IsEmpty;
        }
    }

    // Whether any of the note's vibrato stretches sweeps its pitch.
    public bool HasVibrato
    {
        get
        {
            return Vibratos.Any(vibrato => vibrato.IsActive);
        }
    }

    // Whether any of the note's tremolo stretches ducks its volume.
    public bool HasTremolo
    {
        get
        {
            return Tremolos.Any(tremolo => tremolo.IsActive);
        }
    }

    // The byte an MML q would write for this note.
    public int Quantization
    {
        get
        {
            return QuantizationOf(Gate, Velocity);
        }
    }

    // The q byte a gate and a velocity are written as together.
    public static int QuantizationOf(int gate, int velocity)
    {
        return gate << QuantizationGateShift | velocity;
    }

    // The gate half of a q byte.
    public static int GateOfQuantization(int quantization)
    {
        return quantization >> QuantizationGateShift;
    }

    // The velocity half of a q byte.
    public static int VelocityOfQuantization(int quantization)
    {
        return quantization & QuantizationVelocityMask;
    }

    // The gate and the velocity once one half of the q has been set to a value, the other moved off 0 where both would be 0.
    public static (int Gate, int Velocity) QuantizationWithHalfSet(NoteDefault setHalf, int value, int gate, int velocity)
    {
        if (setHalf == NoteDefault.Gate)
        {
            return QuantizationKeeping(setHalf, value, velocity);
        }

        return QuantizationKeeping(setHalf, gate, value);
    }

    // The gate and the velocity with one half kept as it is and the other moved off 0 where both would be 0.
    public static (int Gate, int Velocity) QuantizationKeeping(NoteDefault keptHalf, int gate, int velocity)
    {
        if (keptHalf == NoteDefault.Gate)
        {
            return (gate, UneditedQuantizationHalf(gate, velocity));
        }

        return (UneditedQuantizationHalf(velocity, gate), velocity);
    }

    // The note at a new length, a level bend node on its old end moving onto the new one.
    public Note WithLength(int lengthTicks)
    {
        int oldLengthTicks = LengthTicks;
        return this with { LengthTicks = lengthTicks, FlatBendNodeTicks = [.. FlatBendNodeTicks.Select(tick => tick == oldLengthTicks ? lengthTicks : tick).Distinct().Order()] };
    }

    // This note cut down to a new start and length as a split leaves it, its sweeps, its volume and pan chains and its envelope rows rebased onto what
    // survives, so what is heard stays where it was in the song.
    public Note CutToSpan(int startTick, int lengthTicks)
    {
        int trimmedTicks = startTick - StartTick;
        var cutNote = WithLength(lengthTicks) with
        {
            StartTick = startTick,
            Vibratos = NoteSweep.CutToSpan(Vibratos, trimmedTicks, lengthTicks),
            Tremolos = NoteSweep.CutToSpan(Tremolos, trimmedTicks, lengthTicks),
            Pans = NotePan.Lane.TrimHead(Pans, trimmedTicks, LengthTicks),
            Volumes = NoteVolume.Lane.TrimHead(Volumes, trimmedTicks, LengthTicks),
            Envelopes = EnvelopeRows.TrimHead(Envelopes, trimmedTicks),
        };

        return cutNote.FittedToLength();
    }

    // The note over a different stretch, as an edit leaves it: everything it carries kept where it stands from its start, and cut to what its length reaches.
    public Note WithSpan(int startTick, int lengthTicks)
    {
        return (WithLength(lengthTicks) with { StartTick = startTick }).FittedToLength();
    }

    // The note with anything its own length cannot reach taken off its chains and its level bend nodes.
    public Note FittedToLength()
    {
        var fittedBends = PitchBend.FitToNoteLength(Bends, LengthTicks);
        int lengthTicks = LengthTicks;
        ImmutableArray<int> fittedFlatBendNodeTicks = [.. FlatBendNodeTicks.Where(tick => BendNodes.IsTickOnNote(tick, lengthTicks))];
        var fittedVibratos = NoteSweep.FitToNoteLength(Vibratos, LengthTicks);
        var fittedTremolos = NoteSweep.FitToNoteLength(Tremolos, LengthTicks);
        var fittedPans = OffsetRows.FitToNoteLength(Pans, LengthTicks);
        var fittedVolumes = OffsetRows.FitToNoteLength(Volumes, LengthTicks);
        var fittedEnvelopes = OffsetRows.FitToNoteLength(Envelopes, LengthTicks);
        bool isUnchangedByFitting = fittedBends.SequenceEqual(Bends)
            && fittedFlatBendNodeTicks.Length == FlatBendNodeTicks.Length
            && fittedVibratos.SequenceEqual(Vibratos)
            && fittedTremolos.SequenceEqual(Tremolos)
            && fittedPans.SequenceEqual(Pans)
            && fittedVolumes.SequenceEqual(Volumes)
            && fittedEnvelopes.Length == Envelopes.Length;
        if (isUnchangedByFitting)
        {
            return this;
        }

        return this with { Bends = fittedBends, FlatBendNodeTicks = fittedFlatBendNodeTicks, Vibratos = fittedVibratos, Tremolos = fittedTremolos, Pans = fittedPans, Volumes = fittedVolumes, Envelopes = fittedEnvelopes };
    }

    // The note with the last stretch of its vibrato, tremolo and pitch bend chains running on to its end wherever it already ends exactly there.
    public Note WithSweepTailsRunToEnd()
    {
        return this with
        {
            Vibratos = NoteSweep.WithEndlessTail(Vibratos, LengthTicks),
            Tremolos = NoteSweep.WithEndlessTail(Tremolos, LengthTicks),
            Bends = NoteSweep.WithEndlessTail(Bends, LengthTicks),
        };
    }

    // Whether two notes hold the same values, their chains compared element by element.
    public bool Equals(Note other)
    {
        return StartTick == other.StartTick
            && LengthTicks == other.LengthTicks
            && Semitone == other.Semitone
            && InstrumentId == other.InstrumentId
            && Gate == other.Gate
            && Velocity == other.Velocity
            && FineTune == other.FineTune
            && IsLegato == other.IsLegato
            && IsHeld == other.IsHeld
            && PanSource == other.PanSource
            && VolumeSource == other.VolumeSource
            && Amplify == other.Amplify
            && AmplifySource == other.AmplifySource
            && IsEchoOn == other.IsEchoOn
            && EchoSource == other.EchoSource
            && GateSource == other.GateSource
            && VelocitySource == other.VelocitySource
            && FineTuneSource == other.FineTuneSource
            && VibratoSource == other.VibratoSource
            && TremoloSource == other.TremoloSource
            && BendSource == other.BendSource
            && EnvelopeSource == other.EnvelopeSource
            && LegatoSource == other.LegatoSource
            && HoldSource == other.HoldSource
            && Bends.SequenceEqual(other.Bends)
            && FlatBendNodeTicks.SequenceEqual(other.FlatBendNodeTicks)
            && Vibratos.SequenceEqual(other.Vibratos)
            && Tremolos.SequenceEqual(other.Tremolos)
            && Pans.SequenceEqual(other.Pans)
            && Volumes.SequenceEqual(other.Volumes)
            && Envelopes.SequenceEqual(other.Envelopes);
    }

    // A hash over the same values the equality compares, the chains element by element.
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StartTick);
        hash.Add(LengthTicks);
        hash.Add(Semitone);
        hash.Add(InstrumentId);
        hash.Add(Gate);
        hash.Add(Velocity);
        hash.Add(FineTune);
        hash.Add(IsLegato);
        hash.Add(IsHeld);
        hash.Add(PanSource);
        hash.Add(VolumeSource);
        hash.Add(Amplify);
        hash.Add(AmplifySource);
        hash.Add(IsEchoOn);
        hash.Add(EchoSource);
        hash.Add(GateSource);
        hash.Add(VelocitySource);
        hash.Add(FineTuneSource);
        hash.Add(VibratoSource);
        hash.Add(TremoloSource);
        hash.Add(BendSource);
        hash.Add(EnvelopeSource);
        hash.Add(LegatoSource);
        hash.Add(HoldSource);
        foreach (var bend in Bends)
        {
            hash.Add(bend);
        }

        foreach (int tick in FlatBendNodeTicks)
        {
            hash.Add(tick);
        }

        foreach (var vibrato in Vibratos)
        {
            hash.Add(vibrato);
        }

        foreach (var tremolo in Tremolos)
        {
            hash.Add(tremolo);
        }

        foreach (var pan in Pans)
        {
            hash.Add(pan);
        }

        foreach (var volume in Volumes)
        {
            hash.Add(volume);
        }

        foreach (var envelope in Envelopes)
        {
            hash.Add(envelope);
        }

        return hash.ToHashCode();
    }

    // The half of a q that was not edited, once it has made room for the half that was: itself, or 1 where both would otherwise be 0.
    private static int UneditedQuantizationHalf(int editedHalf, int uneditedHalf)
    {
        return editedHalf == 0 && uneditedHalf == 0 ? 1 : uneditedHalf;
    }
}
