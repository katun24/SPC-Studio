using System.Collections.Immutable;
using SpcStudio.Audio;

namespace SpcStudio.Model;

// An AddmusicK instrument definition: the five #instruments bytes, the DAW's own name and color, and the defaults a note standing on the instrument's rung plays.
public sealed class Instrument
{
    public const double MaxTuningMultiplier = byte.MaxValue + byte.MaxValue / TuningLowStepsPerUnit;
    public const double TuningLowStepsPerUnit = 256.0;
    public const double NoiseTuningMultiplier = 1.0;
    public const string NoiseNameStem = Sample.NoiseName;
    private const byte NewInstrumentAdsr1 = 0x8F;
    private const byte NewInstrumentAdsr2 = 0xE0;
    private const byte NewInstrumentGain = 0x00;

    // Identifies the instrument for the notes that use it, stable for the instrument's life.
    public required int Id { get; init; }

    public required string Name { get; set; }
    public required int SampleId { get; set; }
    public required int ColorRgb { get; set; }
    public byte Adsr1 { get; set; }
    public byte Adsr2 { get; set; }
    public byte Gain { get; set; }

    // The envelope rows a note standing on this instrument's rung plays after its key-on, each taking over at its tick counted from the note's own start.
    public ImmutableArray<EnvelopeRow> EnvelopeChanges { get; set; } = [];

    public byte TuningHigh { get; set; }
    public byte TuningLow { get; set; }

    // The pan rows a note standing on this instrument's rung plays, the first at the note's start and the last running to its end.
    public ImmutableArray<NotePanRegion> PanRows { get; set; } = [new(OffsetRows.FirstOffsetTicks, PanState.Default)];

    // The channel volume rows a note standing on this instrument's rung plays, the first at the note's start and the last running to its end.
    public ImmutableArray<NoteVolumeRegion> VolumeRows { get; set; } = [new(OffsetRows.FirstOffsetTicks, SnesVolume.DefaultChannelVolume)];

    public int Gate { get; set; } = SnesTiming.DefaultGate;
    public int Velocity { get; set; } = SnesVolume.DefaultVelocity;
    public int FineTune { get; set; } = SnesPitch.MinFineTune;
    public int Amplify { get; set; } = SnesVolume.DefaultAmplify;
    public bool IsEchoOn { get; set; } = SnesEcho.IsChannelEchoOnByDefault;

    // The vibrato stretches a note standing on this instrument's rung plays, counted from each note's own start.
    public ImmutableArray<NoteVibrato> Vibratos { get; set; } = [];

    // The tremolo stretches a note standing on this instrument's rung plays, counted from each note's own start.
    public ImmutableArray<NoteTremolo> Tremolos { get; set; } = [];

    // The $DD chain a note standing on this instrument's rung plays, counted from each note's own start and pitch.
    public ImmutableArray<PitchBend> Bends { get; set; } = [];

    public bool IsLegato { get; set; }
    public bool IsHeld { get; set; }

    // Whether this instrument plays the DSP's noise generator instead of a sample.
    public bool IsNoise
    {
        get
        {
            return SampleId == Audio.Sample.NoiseId;
        }
    }

    // The three envelope bytes together.
    public EnvelopeState Envelope
    {
        get
        {
            return new(Adsr1, Adsr2, Gain);
        }

        set
        {
            Adsr1 = (byte)value.Adsr1;
            Adsr2 = (byte)value.Adsr2;
            Gain = (byte)value.Gain;
        }
    }

    // The five #instruments bytes together.
    public InstrumentBytes Bytes
    {
        get
        {
            return new(Adsr1, Adsr2, Gain, TuningHigh, TuningLow);
        }

        set
        {
            Adsr1 = value.Adsr1;
            Adsr2 = value.Adsr2;
            Gain = value.Gain;
            TuningHigh = value.TuningHigh;
            TuningLow = value.TuningLow;
        }
    }

    // The tuning bytes as a single 8.8 fixed-point number, clamped to what the pair can hold: the tuning at the sample's original rate.
    public double TuningMultiplier
    {
        get
        {
            return MultiplierOf(TuningHigh, TuningLow);
        }

        set
        {
            (TuningHigh, TuningLow) = TuningBytesOf(value);
        }
    }

    // ADSR mode, from ADSR1's top bit, with which the envelope comes from the four ADSR fields rather than from GAIN.
    public bool UsesAdsr
    {
        get
        {
            return Envelope.UsesAdsr;
        }

        set
        {
            Envelope = Envelope.WithUsesAdsr(value);
        }
    }

    // ADSR1's attack rate.
    public int AttackRate
    {
        get
        {
            return Envelope.AttackRate;
        }

        set
        {
            Envelope = Envelope.WithAttackRate(value);
        }
    }

    // ADSR1's decay rate.
    public int DecayRate
    {
        get
        {
            return Envelope.DecayRate;
        }

        set
        {
            Envelope = Envelope.WithDecayRate(value);
        }
    }

    // ADSR2's sustain level.
    public int SustainLevel
    {
        get
        {
            return Envelope.SustainLevel;
        }

        set
        {
            Envelope = Envelope.WithSustainLevel(value);
        }
    }

    // ADSR2's sustain rate.
    public int SustainRate
    {
        get
        {
            return Envelope.SustainRate;
        }

        set
        {
            Envelope = Envelope.WithSustainRate(value);
        }
    }

    // Which of the five things GAIN does, a change re-clamping the value to what the new mode can hold.
    public GainMode GainMode
    {
        get
        {
            return Envelope.GainMode;
        }

        set
        {
            Envelope = Envelope.WithGainMode(value);
        }
    }

    // A level in Direct mode, a rate in the four sloped ones.
    public int GainValue
    {
        get
        {
            return Envelope.GainValue;
        }

        set
        {
            Envelope = Envelope.WithGainValue(value);
        }
    }

    // The largest GAIN value the current mode can hold.
    public int MaxGainValue
    {
        get
        {
            return Envelope.MaxGainValue;
        }
    }

    // An instrument holding nothing but what every new one starts on, for reading those starting values.
    public static Instrument NewInstrumentDefaults
    {
        get
        {
            return Create(default, "", Audio.Sample.NoiseId, default, NoiseTuningMultiplier);
        }
    }

    // A new instrument on the starting envelope bytes, at the tuning its sample measured.
    public static Instrument Create(int id, string name, int sampleId, int colorRgb, double tuningMultiplier)
    {
        var instrument = new Instrument
        {
            Id = id,
            Name = name,
            SampleId = sampleId,
            ColorRgb = colorRgb,
            Adsr1 = NewInstrumentAdsr1,
            Adsr2 = NewInstrumentAdsr2,
            Gain = NewInstrumentGain,
        };
        instrument.TuningMultiplier = tuningMultiplier;
        return instrument;
    }

    // The tuning a sample at a fraction of its original rate sounds this instrument's pitch with, as the two tuning bytes hold it.
    public double TuningMultiplierAt(double rateFractionOfOriginal)
    {
        var (tuningHigh, tuningLow) = TuningBytesOf(TuningMultiplier * rateFractionOfOriginal);
        return MultiplierOf(tuningHigh, tuningLow);
    }

    // The five hex bytes as they appear in an #instruments block, the tuning's for a sample at a fraction of its original rate.
    public string ToMmlBytes(double rateFractionOfOriginal)
    {
        var (tuningHigh, tuningLow) = TuningBytesOf(TuningMultiplier * rateFractionOfOriginal);
        return $"${Adsr1:X2} ${Adsr2:X2} ${Gain:X2} ${tuningHigh:X2} ${tuningLow:X2}";
    }

    // The 8.8 fixed-point number two tuning bytes make.
    private static double MultiplierOf(byte tuningHigh, byte tuningLow)
    {
        return tuningHigh + tuningLow / TuningLowStepsPerUnit;
    }

    // The two tuning bytes nearest a multiplier, clamped to what the pair can hold.
    private static (byte TuningHigh, byte TuningLow) TuningBytesOf(double multiplier)
    {
        double clampedMultiplier = Math.Clamp(multiplier, 0, MaxTuningMultiplier);
        byte tuningHigh = (byte)clampedMultiplier;
        return (tuningHigh, (byte)Math.Min(byte.MaxValue, Math.Round((clampedMultiplier - tuningHigh) * TuningLowStepsPerUnit)));
    }

    // Whether another instrument holds all the same values, compared field by field.
    public bool HasSameValuesAs(Instrument other)
    {
        return Id == other.Id
            && Name == other.Name
            && SampleId == other.SampleId
            && ColorRgb == other.ColorRgb
            && PanRows.SequenceEqual(other.PanRows)
            && VolumeRows.SequenceEqual(other.VolumeRows)
            && Gate == other.Gate
            && Velocity == other.Velocity
            && FineTune == other.FineTune
            && Amplify == other.Amplify
            && IsEchoOn == other.IsEchoOn
            && Vibratos.SequenceEqual(other.Vibratos)
            && Tremolos.SequenceEqual(other.Tremolos)
            && Bends.SequenceEqual(other.Bends)
            && EnvelopeChanges.SequenceEqual(other.EnvelopeChanges)
            && IsLegato == other.IsLegato
            && IsHeld == other.IsHeld
            && HasSameSoundAs(other);
    }

    // Whether two instruments would produce the same #instruments entry: the same sample and the same five bytes.
    public bool HasSameSoundAs(Instrument other)
    {
        return SampleId == other.SampleId && Bytes == other.Bytes;
    }

    // Whether the other instrument's two tuning bytes are this one's.
    public bool HasSameTuningAs(Instrument other)
    {
        return TuningHigh == other.TuningHigh && TuningLow == other.TuningLow;
    }

    // An independent copy, id and all.
    public Instrument Clone()
    {
        return CopyAs(Id, Name, ColorRgb);
    }

    // An independent copy under another id, name and color.
    public Instrument CopyAs(int id, string name, int colorRgb)
    {
        return new()
        {
            Id = id,
            Name = name,
            SampleId = SampleId,
            ColorRgb = colorRgb,
            Bytes = Bytes,
            EnvelopeChanges = EnvelopeChanges,
            PanRows = PanRows,
            VolumeRows = VolumeRows,
            Gate = Gate,
            Velocity = Velocity,
            FineTune = FineTune,
            Amplify = Amplify,
            IsEchoOn = IsEchoOn,
            Vibratos = Vibratos,
            Tremolos = Tremolos,
            Bends = Bends,
            IsLegato = IsLegato,
            IsHeld = IsHeld,
        };
    }
}
