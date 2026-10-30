namespace SpcStudio.Model;

// The DSP's own register map: where each voice's registers and the chip's global ones sit, what a voice's registers hold in a register image, and what a $F6 write to each register means to the model.
public static class SnesDspRegister
{
    public const int RegisterCount = 0x80;
    public const int VoiceCount = RegisterCount >> VoiceShift;
    public const int AllVoicesMask = (1 << VoiceCount) - 1;
    public const int DefaultMasterVolume = 0x7F;
    public const int FlagsResetAndMute = 0xC0;
    public const int FlagsEchoWriteOff = 0x20;
    public const int FlagsNoiseClock = 0x1F;
    public const int MaxEnvelopeLevel = 0x7F;
    public const int VolumeLeftColumn = 0x0;
    public const int VolumeRightColumn = 0x1;
    public const int PitchLowColumn = 0x2;
    public const int PitchHighColumn = 0x3;
    public const int SourceColumn = 0x4;
    public const int Adsr1Column = 0x5;
    public const int Adsr2Column = 0x6;
    public const int GainColumn = 0x7;
    public const int EnvelopeLevelColumn = 0x8;
    public const int EchoCoefficientColumn = 0xF;
    public const int EchoFeedback = 0x0D;
    public const int PitchModulation = 0x2D;
    public const int NoiseVoices = 0x3D;
    public const int MasterVolumeLeft = 0x0C;
    public const int MasterVolumeRight = 0x1C;
    public const int EchoVolumeLeft = 0x2C;
    public const int EchoVolumeRight = 0x3C;
    public const int KeyOn = 0x4C;
    public const int VoiceEnds = 0x7C;
    public const int EchoSends = 0x4D;
    public const int SampleDirectory = 0x5D;
    public const int Flags = 0x6C;
    public const int EchoDelay = 0x7D;
    private const int SampleOutputColumn = 0x9;
    private const int UnusedGlobalRegister = 0x1D;
    private const int VoiceShift = 4;
    private const int ColumnMask = 0x0F;

    // The columns of the map the chip leaves unused in every row.
    private static readonly int[] UnusedColumns = [0xA, 0xB, 0xE];

    // Which voice a register belongs to, for the registers that belong to one.
    public static int VoiceOf(int register)
    {
        return register >> VoiceShift;
    }

    // The address of one of a voice's registers, or of a global register in that column.
    public static int AddressOf(int voice, int column)
    {
        return (voice << VoiceShift) | column;
    }

    // What a write to a register means to the model.
    public static DspRegisterKind Classify(int register)
    {
        if (register < 0 || register >= RegisterCount)
        {
            return DspRegisterKind.Other;
        }

        int column = ColumnOf(register);
        return register switch
        {
            _ when column is Adsr1Column or Adsr2Column or GainColumn => DspRegisterKind.Envelope,
            MasterVolumeLeft or MasterVolumeRight => DspRegisterKind.MasterVolume,
            EchoVolumeLeft => DspRegisterKind.EchoVolumeLeft,
            Flags => DspRegisterKind.Flags,
            EchoVolumeRight => DspRegisterKind.EchoVolumeRight,
            EchoFeedback => DspRegisterKind.EchoFeedback,
            PitchModulation => DspRegisterKind.PitchModulation,
            _ when column == EchoCoefficientColumn => DspRegisterKind.EchoCoefficient,
            VoiceEnds => DspRegisterKind.Status,
            _ when column is EnvelopeLevelColumn or SampleOutputColumn => DspRegisterKind.Status,
            UnusedGlobalRegister => DspRegisterKind.Unused,
            _ when UnusedColumns.Contains(column) => DspRegisterKind.Unused,
            _ => DspRegisterKind.Other,
        };
    }

    // Which envelope byte an envelope register is.
    public static EnvelopeSetting EnvelopeSettingOf(int register)
    {
        return ColumnOf(register) switch
        {
            Adsr1Column => EnvelopeSetting.Adsr1,
            Adsr2Column => EnvelopeSetting.Adsr2,
            _ => EnvelopeSetting.Gain,
        };
    }

    // Which FIR tap a coefficient register is, which is the voice column it sits in.
    public static int FirTapOf(int register)
    {
        return VoiceOf(register);
    }

    // Whether a voice's bit is set in a register that holds one bit a voice, KON and EON among them.
    public static bool HasVoiceBit(int mask, int voice)
    {
        return (mask & VoiceBit(voice)) != 0;
    }

    // The bit a voice has in a register that holds one bit per voice, such as KON.
    public static int VoiceBit(int voice)
    {
        return 1 << voice;
    }

    // The PITCH a voice's two pitch registers hold in a register image.
    public static int PitchOf(ReadOnlySpan<byte> registers, int voice)
    {
        return (registers[AddressOf(voice, PitchLowColumn)] | (registers[AddressOf(voice, PitchHighColumn)] << ByteUnits.BitsPerByte)) & SnesPitch.MaxPitchRegister;
    }

    // The sample directory entry a voice's SRCN names in a register image.
    public static int SourceOf(ReadOnlySpan<byte> registers, int voice)
    {
        return registers[AddressOf(voice, SourceColumn)];
    }

    // A voice's ADSR1, ADSR2 and GAIN in a register image.
    public static EnvelopeState EnvelopeOf(ReadOnlySpan<byte> registers, int voice)
    {
        return new(registers[AddressOf(voice, Adsr1Column)], registers[AddressOf(voice, Adsr2Column)], registers[AddressOf(voice, GainColumn)]);
    }

    // The level a voice's envelope has reached in a register image, ENVX's level bits.
    public static int EnvelopeLevelOf(ReadOnlySpan<byte> registers, int voice)
    {
        return registers[AddressOf(voice, EnvelopeLevelColumn)] & MaxEnvelopeLevel;
    }

    // Whether a voice plays the noise rather than its sample in a register image, its NON bit.
    public static bool IsPlayingNoise(ReadOnlySpan<byte> registers, int voice)
    {
        return HasVoiceBit(registers[NoiseVoices], voice);
    }

    // A voice's VOL pair in a register image, both sides signed.
    public static VoiceVolume VolumeOf(ReadOnlySpan<byte> registers, int voice)
    {
        return new((sbyte)registers[AddressOf(voice, VolumeLeftColumn)], (sbyte)registers[AddressOf(voice, VolumeRightColumn)]);
    }

    // Which of a voice's registers a register is, or which column of the global ones.
    private static int ColumnOf(int register)
    {
        return register & ColumnMask;
    }
}
