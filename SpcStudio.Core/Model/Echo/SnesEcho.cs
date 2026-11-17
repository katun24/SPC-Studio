namespace SpcStudio.Model;

// The driver's echo unit: what its commands accept, the FIR presets they choose between, how a filter's coefficients travel as one value, and what a delay step costs.
public static class SnesEcho
{
    public const bool IsEnabledByDefault = false;
    public const bool IsChannelEchoOnByDefault = false;
    public const int MinVolume = 0;
    public const int MaxVolume = 255;
    public const int DefaultVolume = 0;
    public const int MinDelay = 0;
    public const int MaxDelay = 15;
    public const int DefaultDelay = 0;
    public const int MinFeedback = 0;
    public const int MaxFeedback = 255;
    public const int DefaultFeedback = 0;
    public const int MinFilter = 0;
    public const int MaxFilter = 1;
    public const int PresetCount = MaxFilter - MinFilter + 1;
    public const int DefaultFilter = 0;
    public const int TapCount = 8;
    public const int FilterWindowBytes = 256;
    public const int SamplesPerDelayStep = 512;
    public const int BytesPerSample = 4;
    public const int BytesPerDelayStep = BytesPerSample * SamplesPerDelayStep;
    public const int MillisecondsPerDelayStep = 16;
    private const int BitsPerCoefficient = 8;
    private const int MinBufferSamples = 1;

    public static readonly EchoSetting[] Settings = Enum.GetValues<EchoSetting>();
    public static readonly EchoSetting[] VolumeSettings = [EchoSetting.VolumeLeft, EchoSetting.VolumeRight];
    public static readonly ValueRange VolumeRange = new(MinVolume, MaxVolume);
    public static readonly ValueRange DelayRange = new(MinDelay, MaxDelay);
    public static readonly ValueRange FeedbackRange = new(MinFeedback, MaxFeedback);
    public static readonly ValueRange FilterRange = new(MinFilter, MaxFilter);

    // The FIR presets, as the signed coefficients each writes into the FIR registers.
    public static readonly sbyte[][] PresetCoefficients =
    [
        [unchecked((sbyte)0xFF), 0x08, 0x17, 0x24, 0x24, 0x17, 0x08, unchecked((sbyte)0xFF)],
        [0x7F, 0, 0, 0, 0, 0, 0, 0],
    ];

    // The presets packed, which are the first entries of a song's filter table.
    public static readonly long[] PackedPresets = [.. PresetCoefficients.Select(PackCoefficients)];

    // A filter's coefficients as one value, coefficient 0 in the low byte.
    public static long PackCoefficients(IReadOnlyList<sbyte> coefficients)
    {
        long packedCoefficients = 0;
        for (int tap = 0; tap < TapCount; tap++)
        {
            packedCoefficients |= (long)(byte)coefficients[tap] << (BitsPerCoefficient * tap);
        }

        return packedCoefficients;
    }

    // A packed filter's coefficients, coefficient 0 first.
    public static sbyte[] UnpackCoefficients(long packedCoefficients)
    {
        var coefficients = new sbyte[TapCount];
        for (int tap = 0; tap < TapCount; tap++)
        {
            coefficients[tap] = CoefficientAt(packedCoefficients, tap);
        }

        return coefficients;
    }

    // One coefficient out of a packed filter.
    public static sbyte CoefficientAt(long packedCoefficients, int tap)
    {
        return (sbyte)(byte)(packedCoefficients >> (BitsPerCoefficient * tap));
    }

    // The same packed filter with one coefficient replaced, which is what a $F6 to one FIR register leaves.
    public static long WithCoefficient(long packedCoefficients, int tap, int value)
    {
        int shift = BitsPerCoefficient * tap;
        return (packedCoefficients & ~(0xFFL << shift)) | ((long)(byte)value << shift);
    }

    // The byte $F1 writes for a filter index: a preset writes itself, and a set of the song's own states the first preset for the $F5 behind it to overwrite.
    public static int PresetByteFor(int filterIndex)
    {
        return IsCustomFilter(filterIndex) ? MinFilter : filterIndex;
    }

    // Whether a filter index names a set of the song's own, which only $F5 can write.
    public static bool IsCustomFilter(int filterIndex)
    {
        return filterIndex > MaxFilter;
    }

    // Where $F1's third byte reads its coefficients from, counted in bytes from the driver's first preset table: the index times the tap count, kept to a byte.
    public static int FilterOffsetOf(int filterIndex)
    {
        return filterIndex * TapCount & byte.MaxValue;
    }

    // How many stereo samples a delay's buffer holds, which is never none.
    public static int BufferSamplesFor(int delay)
    {
        return Math.Max(MinBufferSamples, delay * SamplesPerDelayStep);
    }

    // The value a byte stands for where the DSP reads the register as signed.
    public static int SignedValueOf(int registerByte)
    {
        return (sbyte)registerByte;
    }

    // The range one of the five parameters accepts.
    public static ValueRange Range(EchoSetting setting)
    {
        return setting switch
        {
            EchoSetting.Delay => DelayRange,
            EchoSetting.Feedback => FeedbackRange,
            EchoSetting.Filter => FilterRange,
            _ => VolumeRange,
        };
    }

    // What a new song's parameter starts at.
    public static int Default(EchoSetting setting)
    {
        return setting switch
        {
            EchoSetting.Delay => DefaultDelay,
            EchoSetting.Feedback => DefaultFeedback,
            EchoSetting.Filter => DefaultFilter,
            _ => DefaultVolume,
        };
    }
}
