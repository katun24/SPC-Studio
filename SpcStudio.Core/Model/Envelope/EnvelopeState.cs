namespace SpcStudio.Model;

// The whole of one channel's envelope at a moment: the three bytes the DSP reads, whether they came off an instrument or out of an $ED, and the layout of the fields inside them.
public readonly record struct EnvelopeState(int Adsr1, int Adsr2, int Gain)
{
    public const int AdsrEnableBit = 0x80;
    public const int MaxAttackRate = 15;
    public const int MaxDecayRate = 7;
    public const int MaxSustainLevel = 7;
    public const int MaxSustainRate = 31;
    private const int AttackRateShift = 0;
    private const int DecayRateShift = 4;
    private const int SustainLevelShift = 5;
    private const int SustainRateShift = 0;

    // An envelope of zeroes, which is what a channel with no notes at all has.
    public static readonly EnvelopeState Unset = new(0, 0, 0);

    // What an override unit placed where the channel plays nothing starts on: ADSR at the fastest attack and the fastest decay, to a full sustain, and no release.
    public static readonly EnvelopeState FreshUnit = Unset
        .WithUsesAdsr(true)
        .WithAttackRate(MaxAttackRate)
        .WithDecayRate(MaxDecayRate)
        .WithSustainLevel(MaxSustainLevel)
        .WithSustainRate(0);

    // The value one of the three bytes holds.
    public int this[EnvelopeSetting setting]
    {
        get
        {
            return setting switch
            {
                EnvelopeSetting.Adsr1 => Adsr1,
                EnvelopeSetting.Adsr2 => Adsr2,
                _ => Gain,
            };
        }
    }

    // Whether the DSP runs ADSR rather than GAIN.
    public bool UsesAdsr
    {
        get
        {
            return IsAdsrEnabledBy(Adsr1);
        }
    }

    // ADSR1's attack rate.
    public int AttackRate
    {
        get
        {
            return FieldOf(Adsr1, AttackRateShift, MaxAttackRate);
        }
    }

    // ADSR1's decay rate.
    public int DecayRate
    {
        get
        {
            return FieldOf(Adsr1, DecayRateShift, MaxDecayRate);
        }
    }

    // ADSR2's sustain level.
    public int SustainLevel
    {
        get
        {
            return FieldOf(Adsr2, SustainLevelShift, MaxSustainLevel);
        }
    }

    // ADSR2's sustain rate.
    public int SustainRate
    {
        get
        {
            return FieldOf(Adsr2, SustainRateShift, MaxSustainRate);
        }
    }

    // Which of the five things GAIN does.
    public GainMode GainMode
    {
        get
        {
            return GainByte.ModeOf(Gain);
        }
    }

    // The GAIN level in Direct mode, or the rate in the four sloped ones.
    public int GainValue
    {
        get
        {
            return GainByte.ValueOf(Gain);
        }
    }

    // The largest GAIN value the current mode can hold.
    public int MaxGainValue
    {
        get
        {
            return GainByte.MaxValueOf(GainMode);
        }
    }

    // Whether an ADSR1 byte runs the envelope as ADSR rather than GAIN.
    public static bool IsAdsrEnabledBy(int adsr1)
    {
        return (adsr1 & AdsrEnableBit) != 0;
    }

    // This envelope as the DSP plays it: itself under GAIN, and with its GAIN byte cleared under ADSR, which reads none of it.
    public EnvelopeState Audible()
    {
        return UsesAdsr ? this with { Gain = 0 } : this;
    }

    // The same envelope with one of the three bytes replaced.
    public EnvelopeState With(EnvelopeSetting setting, int value)
    {
        return setting switch
        {
            EnvelopeSetting.Adsr1 => this with { Adsr1 = value },
            EnvelopeSetting.Adsr2 => this with { Adsr2 = value },
            _ => this with { Gain = value },
        };
    }

    // The same envelope running ADSR or GAIN.
    public EnvelopeState WithUsesAdsr(bool usesAdsr)
    {
        return this with { Adsr1 = usesAdsr ? Adsr1 | AdsrEnableBit : Adsr1 & ~AdsrEnableBit };
    }

    // The same envelope with another attack rate, clamped to what the field holds.
    public EnvelopeState WithAttackRate(int attackRate)
    {
        return this with { Adsr1 = WithField(Adsr1, AttackRateShift, MaxAttackRate, attackRate) };
    }

    // The same envelope with another decay rate, clamped to what the field holds.
    public EnvelopeState WithDecayRate(int decayRate)
    {
        return this with { Adsr1 = WithField(Adsr1, DecayRateShift, MaxDecayRate, decayRate) };
    }

    // The same envelope with another sustain level, clamped to what the field holds.
    public EnvelopeState WithSustainLevel(int sustainLevel)
    {
        return this with { Adsr2 = WithField(Adsr2, SustainLevelShift, MaxSustainLevel, sustainLevel) };
    }

    // The same envelope with another sustain rate, clamped to what the field holds.
    public EnvelopeState WithSustainRate(int sustainRate)
    {
        return this with { Adsr2 = WithField(Adsr2, SustainRateShift, MaxSustainRate, sustainRate) };
    }

    // The same envelope with GAIN in another mode, the value clamped to what that mode holds.
    public EnvelopeState WithGainMode(GainMode mode)
    {
        return this with { Gain = GainByte.Compose(mode, GainValue) };
    }

    // The same envelope with another GAIN value in the mode it is in.
    public EnvelopeState WithGainValue(int gainValue)
    {
        return this with { Gain = GainByte.Compose(GainMode, gainValue) };
    }

    // The field of a register byte that starts at a bit and runs as wide as its largest value.
    private static int FieldOf(int registerByte, int shift, int maxValue)
    {
        return (registerByte >> shift) & maxValue;
    }

    // A register byte with one field replaced by a value clamped to what the field holds, the other bits left as they were.
    private static int WithField(int registerByte, int shift, int maxValue, int value)
    {
        return (registerByte & ~(maxValue << shift)) | (Math.Clamp(value, 0, maxValue) << shift);
    }
}
