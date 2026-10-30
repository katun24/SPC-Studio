namespace SpcStudio.Model;

// How many channels the song has: the driver's own, which are always there, and the extra ones after them, which play like the rest but which the driver has no room for.
public sealed partial class Song
{
    public const int DriverChannelCount = SnesDspRegister.VoiceCount;
    public const int MaxChannelCount = 32;
    private const string DriverChannelMessage = "Only an extra channel can be deleted.";
    private const string NoRoomForChannelMessage = "The song already has as many channels as it can hold.";

    // Gives the song the driver's channels and no extra ones.
    public Song()
    {
        SetChannelCount(DriverChannelCount);
    }

    // How many channels the song has, the driver's and the extra ones together.
    public int ChannelCount
    {
        get
        {
            return _notes.Count;
        }
    }

    // Whether the song has room for one more channel.
    public bool CanAddChannel
    {
        get
        {
            return ChannelCount < MaxChannelCount;
        }
    }

    // Whether any extra channel holds a note, a lane change or a lane unit.
    public bool HasExtraChannelContent
    {
        get
        {
            for (int channel = DriverChannelCount; channel < ChannelCount; channel++)
            {
                if (HoldsAnything(channel))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Whether a channel is one past the driver's own.
    public static bool IsExtraChannel(int channel)
    {
        return channel >= DriverChannelCount;
    }

    // Adds a channel after the last, with nothing on it and every base at its default.
    public void AddChannel()
    {
        if (!CanAddChannel)
        {
            throw new InvalidOperationException(NoRoomForChannelMessage);
        }

        _notes.Add([]);
        Muting.AddMember();
        _baseChannelVolumes.Add(SnesVolume.DefaultChannelVolume);
        _channelVolumeChanges.Add([]);
        _basePans.Add(SnesVolume.DefaultPanIndex);
        _baseSurrounds.Add(default);
        _panChanges.Add([]);
        _baseAmplifies.Add(SnesVolume.DefaultAmplify);
        _amplifyChanges.Add([]);
        _isBaseChannelEchoOn.Add(SnesEcho.IsChannelEchoOnByDefault);
        _channelEchoChanges.Add([]);
    }

    // Takes an extra channel out with everything on it, every channel after it moving down one, and the roll and the markers following.
    public void DeleteChannel(int channel)
    {
        if (!IsExtraChannel(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), DriverChannelMessage);
        }

        int previousRollTicks = RollLengthTicks;
        _notes.RemoveAt(channel);
        Muting.RemoveMember(channel);
        _baseChannelVolumes.RemoveAt(channel);
        _channelVolumeChanges.RemoveAt(channel);
        _basePans.RemoveAt(channel);
        _baseSurrounds.RemoveAt(channel);
        _panChanges.RemoveAt(channel);
        _baseAmplifies.RemoveAt(channel);
        _amplifyChanges.RemoveAt(channel);
        _isBaseChannelEchoOn.RemoveAt(channel);
        _channelEchoChanges.RemoveAt(channel);
        FollowRoll(previousRollTicks);
    }

    // Adds channels after the last or deletes the last ones until the song has the number given.
    public void SetChannelCount(int channelCount)
    {
        while (ChannelCount < channelCount)
        {
            AddChannel();
        }

        while (ChannelCount > channelCount)
        {
            DeleteChannel(ChannelCount - 1);
        }
    }

    // Whether a channel holds a note or a change on one of its lanes.
    private bool HoldsAnything(int channel)
    {
        return _notes[channel].Count > 0
            || _channelVolumeChanges[channel].Count > 0
            || _panChanges[channel].Count > 0
            || _amplifyChanges[channel].Count > 0
            || _channelEchoChanges[channel].Count > 0;
    }
}
