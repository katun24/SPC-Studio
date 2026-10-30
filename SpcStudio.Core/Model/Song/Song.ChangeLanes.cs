namespace SpcStudio.Model;

// The song's lanes of changes over a base - the tempo, the song volume, and each channel's volume, pan and amplify - and what every lane of
// changes shares: which ticks may carry one, and how one is put down, taken away and found.
public sealed partial class Song
{
    private const string NotChangeTickMessage = "Not a change tick.";

    private readonly List<LaneDivider<int>> _tempoChanges = [];
    private readonly List<LaneDivider<int>> _volumeChanges = [];
    private readonly List<int> _baseChannelVolumes = [];
    private readonly List<List<LaneDivider<int>>> _channelVolumeChanges = [];
    private readonly List<int> _basePans = [];
    private readonly List<SnesSurround> _baseSurrounds = [];
    private readonly List<List<PanChange>> _panChanges = [];
    private readonly List<int> _baseAmplifies = [];
    private readonly List<List<LaneValueChange>> _amplifyChanges = [];

    // The first tick past what a lane drag covers, which holds the roll open and is null when no drag is running.
    private int? _liftedReachTick;

    public int BaseTempo { get; set; } = SnesTiming.DefaultTempo;
    public int BaseMasterVolume { get; set; } = SnesVolume.DefaultMasterVolume;

    // The tempo changes in tick order, one to a tick, none at tick 0 but a fade.
    public IReadOnlyList<LaneDivider<int>> TempoChanges
    {
        get
        {
            return _tempoChanges;
        }
    }

    // The song volume changes in tick order, one to a tick, none at tick 0 but a fade.
    public IReadOnlyList<LaneDivider<int>> VolumeChanges
    {
        get
        {
            return _volumeChanges;
        }
    }

    // Whether a tick can carry a change in a lane, which is anywhere but the base's own tick 0.
    public static bool IsChangeTick(int tick)
    {
        return tick > 0;
    }

    // Whether a tick can carry a change in a lane that fades, which is tick 0 as well for a change that fades.
    public static bool IsFadeChangeTick(int tick, bool fades)
    {
        return fades ? tick >= 0 : IsChangeTick(tick);
    }

    // Puts a tempo change at a tick, replacing whatever was there, its value at tick 0 being the base's.
    public void SetTempoChange(int tick, int value, bool fades = false, int fadeTo = 0)
    {
        var (changeValue, changeFadeTo) = FadingChangeAsPut(tick, value, fades, fadeTo, BaseTempo, SnesTiming.TempoRange);
        PutChange(_tempoChanges, new LaneDivider<int>(tick, changeValue, fades, changeFadeTo));
    }

    // Removes the tempo change at a tick, and answers whether there was one.
    public bool RemoveTempoChange(int tick)
    {
        return RemoveChange(_tempoChanges, tick);
    }

    // The tempo change at a tick, or null where there is none.
    public LaneDivider<int>? TempoChangeAt(int tick)
    {
        return ChangeAt(_tempoChanges, tick);
    }

    // Replaces the tempo changes as given.
    public void SetTempoChanges(IReadOnlyList<LaneDivider<int>> changes)
    {
        ReplaceContents(_tempoChanges, changes);
    }

    // Puts a song volume change at a tick, replacing whatever was there, its value at tick 0 being the base's.
    public void SetVolumeChange(int tick, int value, bool fades = false, int fadeTo = 0)
    {
        var (changeValue, changeFadeTo) = FadingChangeAsPut(tick, value, fades, fadeTo, BaseMasterVolume, SnesVolume.MasterVolumeRange);
        PutChange(_volumeChanges, new LaneDivider<int>(tick, changeValue, fades, changeFadeTo));
    }

    // Removes the song volume change at a tick, and answers whether there was one.
    public bool RemoveVolumeChange(int tick)
    {
        return RemoveChange(_volumeChanges, tick);
    }

    // The song volume change at a tick, or null where there is none.
    public LaneDivider<int>? VolumeChangeAt(int tick)
    {
        return ChangeAt(_volumeChanges, tick);
    }

    // Replaces the song volume changes as given.
    public void SetVolumeChanges(IReadOnlyList<LaneDivider<int>> changes)
    {
        ReplaceContents(_volumeChanges, changes);
    }

    // The volume a channel plays at from tick 0.
    public int BaseChannelVolume(int channel)
    {
        return _baseChannelVolumes[channel];
    }

    // Sets the volume a channel plays at from tick 0, held to its range.
    public void SetBaseChannelVolume(int channel, int value)
    {
        _baseChannelVolumes[channel] = SnesVolume.ChannelVolumeRange.Clamp(value);
    }

    // A channel's volume changes in tick order, one to a tick, none at tick 0 but a fade.
    public IReadOnlyList<LaneDivider<int>> ChannelVolumeChanges(int channel)
    {
        return _channelVolumeChanges[channel];
    }

    // Puts a channel volume change at a tick, replacing whatever was there, its value at tick 0 being the base's.
    public void SetChannelVolumeChange(int channel, int tick, int value, bool fades = false, int fadeTo = 0)
    {
        var (changeValue, changeFadeTo) = FadingChangeAsPut(tick, value, fades, fadeTo, BaseChannelVolume(channel), SnesVolume.ChannelVolumeRange);
        PutChange(_channelVolumeChanges[channel], new LaneDivider<int>(tick, changeValue, fades, changeFadeTo));
    }

    // Removes a channel's volume change at a tick, and answers whether there was one.
    public bool RemoveChannelVolumeChange(int channel, int tick)
    {
        return RemoveChange(_channelVolumeChanges[channel], tick);
    }

    // A channel's volume change at a tick, or null where there is none.
    public LaneDivider<int>? ChannelVolumeChangeAt(int channel, int tick)
    {
        return ChangeAt(_channelVolumeChanges[channel], tick);
    }

    // Replaces every channel's base volume and volume changes as given.
    public void SetChannelVolumeState(IReadOnlyList<int> baseVolumes, IReadOnlyList<IReadOnlyList<LaneDivider<int>>> changes)
    {
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            _baseChannelVolumes[channel] = baseVolumes[channel];
            ReplaceContents(_channelVolumeChanges[channel], changes[channel]);
        }
    }

    // The pan a channel plays at from tick 0, as an index into the driver's pan table.
    public int BasePan(int channel)
    {
        return _basePans[channel];
    }

    // Sets the pan a channel plays at from tick 0, held to its range.
    public void SetBasePan(int channel, int value)
    {
        _basePans[channel] = SnesVolume.PanIndexRange.Clamp(value);
    }

    // Which sides of a channel play phase-inverted from tick 0.
    public SnesSurround BaseSurround(int channel)
    {
        return _baseSurrounds[channel];
    }

    // Sets which sides of a channel play phase-inverted from tick 0.
    public void SetBaseSurround(int channel, SnesSurround value)
    {
        _baseSurrounds[channel] = value;
    }

    // A channel's pan changes in tick order, one to a tick, none at tick 0 but a fade.
    public IReadOnlyList<PanChange> PanChanges(int channel)
    {
        return _panChanges[channel];
    }

    // Puts a pan change at a tick, replacing whatever was there, its value at tick 0 being the base's and its surround none.
    public void SetPanChange(int channel, int tick, int value, bool fades = false, int fadeTo = 0, SnesSurround surround = default)
    {
        var (changeValue, changeFadeTo) = FadingChangeAsPut(tick, value, fades, fadeTo, BasePan(channel), SnesVolume.PanIndexRange);
        var changeSurround = tick == 0 ? SnesSurround.None : surround;
        PutChange(_panChanges[channel], new PanChange(tick, changeValue, fades, changeFadeTo, changeSurround));
    }

    // Removes a channel's pan change at a tick, and answers whether there was one.
    public bool RemovePanChange(int channel, int tick)
    {
        return RemoveChange(_panChanges[channel], tick);
    }

    // A channel's pan change at a tick, or null where there is none.
    public PanChange? PanChangeAt(int channel, int tick)
    {
        return ChangeAt(_panChanges[channel], tick);
    }

    // Replaces every channel's base pan, base surround and pan changes as given.
    public void SetPanState(IReadOnlyList<int> basePans, IReadOnlyList<SnesSurround> baseSurrounds, IReadOnlyList<IReadOnlyList<PanChange>> changes)
    {
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            _basePans[channel] = basePans[channel];
            _baseSurrounds[channel] = baseSurrounds[channel];
            ReplaceContents(_panChanges[channel], changes[channel]);
        }
    }

    // The amplify a channel plays at from tick 0.
    public int BaseAmplify(int channel)
    {
        return _baseAmplifies[channel];
    }

    // Sets the amplify a channel plays at from tick 0, held to its range.
    public void SetBaseAmplify(int channel, int value)
    {
        _baseAmplifies[channel] = SnesVolume.AmplifyRange.Clamp(value);
    }

    // A channel's amplify changes in tick order, one to a tick, none at tick 0.
    public IReadOnlyList<LaneValueChange> AmplifyChanges(int channel)
    {
        return _amplifyChanges[channel];
    }

    // Puts an amplify change at a tick, replacing whatever was there.
    public void SetAmplifyChange(int channel, int tick, int value)
    {
        ThrowUnlessChangeTick(tick);
        PutChange(_amplifyChanges[channel], new LaneValueChange(tick, SnesVolume.AmplifyRange.Clamp(value)));
    }

    // Removes a channel's amplify change at a tick, and answers whether there was one.
    public bool RemoveAmplifyChange(int channel, int tick)
    {
        return RemoveChange(_amplifyChanges[channel], tick);
    }

    // A channel's amplify change at a tick, or null where there is none.
    public LaneValueChange? AmplifyChangeAt(int channel, int tick)
    {
        return ChangeAt(_amplifyChanges[channel], tick);
    }

    // Replaces every channel's base amplify and amplify changes as given.
    public void SetAmplifyState(IReadOnlyList<int> baseAmplifies, IReadOnlyList<IReadOnlyList<LaneValueChange>> changes)
    {
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            _baseAmplifies[channel] = baseAmplifies[channel];
            ReplaceContents(_amplifyChanges[channel], changes[channel]);
        }
    }

    // The first tick past what a lane drag covers, or null when none is running, the roll and the end marker following it.
    public void SetLiftedReach(int? reachTick)
    {
        int previousRollTicks = RollLengthTicks;
        _liftedReachTick = reachTick;
        FollowRoll(previousRollTicks);
    }

    // Puts a change into a lane's list in tick order, replacing one at its tick, the roll and the markers following.
    private void PutChange<TChange>(List<TChange> changes, TChange change) where TChange : struct, ILaneChange
    {
        int previousRollTicks = RollLengthTicks;
        int insertIndex = changes.FindIndex(existing => existing.Tick >= change.Tick);
        if (insertIndex < 0)
        {
            changes.Add(change);
        }
        else if (changes[insertIndex].Tick == change.Tick)
        {
            changes[insertIndex] = change;
        }
        else
        {
            changes.Insert(insertIndex, change);
        }

        FollowRoll(previousRollTicks);
    }

    // Removes a lane's change at a tick, the roll and the markers following, and answers whether there was one.
    private bool RemoveChange<TChange>(List<TChange> changes, int tick) where TChange : struct, ILaneChange
    {
        int changeIndex = changes.FindIndex(change => change.Tick == tick);
        if (changeIndex < 0)
        {
            return false;
        }

        int previousRollTicks = RollLengthTicks;
        changes.RemoveAt(changeIndex);
        FollowRoll(previousRollTicks);
        return true;
    }

    // A lane's change at a tick, or null where there is none.
    private static TChange? ChangeAt<TChange>(List<TChange> changes, int tick) where TChange : struct, ILaneChange
    {
        int changeIndex = changes.FindIndex(change => change.Tick == tick);
        return changeIndex < 0 ? null : changes[changeIndex];
    }

    // Throws where a tick cannot carry a change.
    private static void ThrowUnlessChangeTick(int tick)
    {
        if (!IsChangeTick(tick))
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, NotChangeTickMessage);
        }
    }

    // Throws where a tick cannot carry a change that fades or does not.
    private static void ThrowUnlessFadeChangeTick(int tick, bool fades)
    {
        if (!IsFadeChangeTick(tick, fades))
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, NotChangeTickMessage);
        }
    }

    // The value and the fade target a change that can fade is put down with, both held to the lane's range and the value at tick 0 the base's own,
    // refusing a tick the change cannot sit on.
    private static (int Value, int FadeTo) FadingChangeAsPut(int tick, int value, bool fades, int fadeTo, int baseValue, ValueRange range)
    {
        ThrowUnlessFadeChangeTick(tick, fades);
        return (ClampedChangeValue(tick, value, baseValue, range), ClampedFadeTo(fades, fadeTo, range));
    }

    // A change's value held to a range, or the base's own value at tick 0.
    private static int ClampedChangeValue(int tick, int value, int baseValue, ValueRange range)
    {
        return tick == 0 ? baseValue : range.Clamp(value);
    }

    // Where a change's ramp lands, held to a range, or nothing where it does not fade.
    private static int ClampedFadeTo(bool fades, int fadeTo, ValueRange range)
    {
        return fades ? range.Clamp(fadeTo) : 0;
    }
}
