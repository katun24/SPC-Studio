namespace SpcStudio.Model;

// The whole of the song's echo at a moment: whether it runs at all, which channels feed it, the five parameters and the filter's coefficients - what one $EF and $F1 pair says.
public readonly record struct EchoState(
    bool IsEnabled,
    int ChannelMask,
    int VolumeLeft,
    int VolumeRight,
    int Delay,
    int Feedback,
    int Filter,
    long PackedCoefficients)
{
    // A song with its echo turned off, which is what $F0 leaves behind.
    public static readonly EchoState Off = new(
        false,
        0,
        SnesEcho.DefaultVolume,
        SnesEcho.DefaultVolume,
        SnesEcho.DefaultDelay,
        SnesEcho.DefaultFeedback,
        SnesEcho.DefaultFilter,
        SnesEcho.PackedPresets[SnesEcho.DefaultFilter]);

    // The value one of the five parameters holds.
    public int this[EchoSetting setting]
    {
        get
        {
            return setting switch
            {
                EchoSetting.VolumeLeft => VolumeLeft,
                EchoSetting.VolumeRight => VolumeRight,
                _ => new EchoUnitState(Delay, Feedback, Filter)[setting],
            };
        }
    }

    // Whether a channel's voice sends to the echo under this state - the song's own switch and the channel's bit in the mask.
    public bool DoesSendFrom(int channel)
    {
        return IsEnabled && SnesDspRegister.HasVoiceBit(ChannelMask, channel);
    }

    // Whether the two would write the same $EF.
    public bool WritesSameSendAs(EchoState other)
    {
        return ChannelMask == other.ChannelMask && VolumeLeft == other.VolumeLeft && VolumeRight == other.VolumeRight;
    }

    // Whether the two would write the same $F1, the filter compared as the byte the command carries.
    public bool WritesSameUnitAs(EchoState other)
    {
        return Delay == other.Delay && Feedback == other.Feedback && SnesEcho.PresetByteFor(Filter) == SnesEcho.PresetByteFor(other.Filter);
    }

    // Whether the two hold the same filter coefficients, whichever index each reached them by.
    public bool HasSameFilterAs(EchoState other)
    {
        return PackedCoefficients == other.PackedCoefficients;
    }
}
