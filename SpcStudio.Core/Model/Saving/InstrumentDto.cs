namespace SpcStudio.Model;

// An instrument's values as both saved files write them, a song's and a single instrument's, each file adding what ties the instrument to it and the song's adding its name.
internal class InstrumentDto
{
    public int Color { get; set; }
    public byte Adsr1 { get; set; }
    public byte Adsr2 { get; set; }
    public byte Gain { get; set; }
    public List<EnvelopeRowDto>? EnvelopeChanges { get; set; }
    public byte TuningHigh { get; set; }
    public byte TuningLow { get; set; }
    public List<PanRegionDto>? PanRows { get; set; }
    public List<VolumeRegionDto>? VolumeRows { get; set; }
    public int Gate { get; set; } = SnesTiming.DefaultGate;
    public int Velocity { get; set; } = SnesVolume.DefaultVelocity;
    public int FineTune { get; set; } = SnesPitch.MinFineTune;
    public int Amplify { get; set; } = SnesVolume.DefaultAmplify;
    public bool Echo { get; set; } = SnesEcho.IsChannelEchoOnByDefault;
    public List<VibratoDto>? Vibratos { get; set; }
    public List<TremoloDto>? Tremolos { get; set; }
    public List<BendDto>? Bends { get; set; }
    public bool Legato { get; set; }
    public bool Hold { get; set; }

    // Fills these values in from an instrument.
    public void CopyFrom(Instrument instrument)
    {
        Color = instrument.ColorRgb;
        Adsr1 = instrument.Adsr1;
        Adsr2 = instrument.Adsr2;
        Gain = instrument.Gain;
        EnvelopeChanges = EnvelopeRowDto.ListOf(instrument.EnvelopeChanges);
        TuningHigh = instrument.TuningHigh;
        TuningLow = instrument.TuningLow;
        PanRows = PanRegionDto.ListOf(instrument.PanRows);
        VolumeRows = VolumeRegionDto.ListOf(instrument.VolumeRows);
        Gate = instrument.Gate;
        Velocity = instrument.Velocity;
        FineTune = instrument.FineTune;
        Amplify = instrument.Amplify;
        Echo = instrument.IsEchoOn;
        Vibratos = VibratoDto.ListOf(instrument.Vibratos);
        Tremolos = TremoloDto.ListOf(instrument.Tremolos);
        Bends = BendDto.ListOf(instrument.Bends);
        Legato = instrument.IsLegato;
        Hold = instrument.IsHeld;
    }

    // Writes these values onto an instrument, bar the name and the color it was made with, refusing any value out of its range.
    public void CopyTo(Instrument instrument, string where)
    {
        SavedFileChecks.RequirePlayedValues(where, Gate, Velocity, FineTune, Amplify);
        var panRows = PanRegionDto.ChainOf(PanRows, where);
        var volumeRows = VolumeRegionDto.ChainOf(VolumeRows, where);
        var vibratos = VibratoDto.ChainOf(Vibratos, where);
        var tremolos = TremoloDto.ChainOf(Tremolos, where);
        var bends = BendDto.ChainOf(Bends, where);
        var (lowestSemitones, highestSemitones) = PitchBend.Reach(bends);

        // The key-on envelope row followed by the saved rows after it.
        List<EnvelopeRowDto> envelopeRows = [new EnvelopeRowDto { Adsr1 = Adsr1, Adsr2 = Adsr2, Gain = Gain }, .. EnvelopeChanges ?? []];

        var envelopes = EnvelopeRowDto.ChainOf(envelopeRows, where);
        if (highestSemitones - lowestSemitones > PitchBend.MaxReachSemitones)
        {
            throw new InvalidDataException(
                $"{where} has a pitch bend chain spanning {highestSemitones - lowestSemitones} semitones, more than the {PitchBend.MaxReachSemitones} the roll holds.");
        }

        instrument.Adsr1 = Adsr1;
        instrument.Adsr2 = Adsr2;
        instrument.Gain = Gain;
        instrument.EnvelopeChanges = [.. envelopes.Skip(1)];
        instrument.TuningHigh = TuningHigh;
        instrument.TuningLow = TuningLow;
        instrument.PanRows = panRows.IsEmpty ? instrument.PanRows : panRows;
        instrument.VolumeRows = volumeRows.IsEmpty ? instrument.VolumeRows : volumeRows;
        instrument.Gate = Gate;
        instrument.Velocity = Velocity;
        instrument.FineTune = FineTune;
        instrument.Amplify = Amplify;
        instrument.IsEchoOn = Echo;
        instrument.Vibratos = vibratos;
        instrument.Tremolos = tremolos;
        instrument.Bends = PitchBend.WithoutLevelBends(bends);
        instrument.IsLegato = Legato;
        instrument.IsHeld = Hold;
    }
}
