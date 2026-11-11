namespace SpcStudio.Model;

// The song's echo: the two echo volumes over their base, the runs the echo plays over, the table of filters those runs name, and each channel's send.
public sealed partial class Song
{
    public const int ChannelEchoOff = 0;
    public const int ChannelEchoOn = 1;
    private const string NotEchoVolumeMessage = "The delay, the feedback and the filter are held on the echo runs.";

    private readonly int[] _baseEchoVolumes = [.. SnesEcho.VolumeSettings.Select(SnesEcho.Default)];
    private readonly List<LaneDivider<int>>[] _echoVolumeChanges = [.. SnesEcho.VolumeSettings.Select(_ => new List<LaneDivider<int>>())];
    private readonly List<bool> _isBaseChannelEchoOn = [];
    private readonly List<EchoRun> _echoRuns = [];
    private readonly List<List<LaneValueChange>> _channelEchoChanges = [];

    // Every filter the song can name, packed, starting with the driver's presets.
    private readonly List<long> _filterTable = [.. SnesEcho.PackedPresets];

    // Whether the song has echo anywhere, which is whether it holds an echo run.
    public bool IsEchoEnabled
    {
        get
        {
            return _echoRuns.Count > 0;
        }
    }

    // The stretches the echo runs over, in start order and never overlapping.
    public IReadOnlyList<EchoRun> EchoRuns
    {
        get
        {
            return _echoRuns;
        }
    }

    // Every filter the song can name, packed: the driver's presets, then the song's own coefficient sets.
    public IReadOnlyList<long> FilterTable
    {
        get
        {
            return _filterTable;
        }
    }

    // The filter indices the song takes, which run to the end of its filter table.
    public ValueRange FilterRange
    {
        get
        {
            return new(SnesEcho.MinFilter, _filterTable.Count - 1);
        }
    }

    // Whether a setting is one of the two echo volumes.
    public static bool IsEchoVolume(EchoSetting setting)
    {
        return setting is EchoSetting.VolumeLeft or EchoSetting.VolumeRight;
    }

    // One of the two echo volumes as the song starts at it.
    public int BaseEchoVolume(EchoSetting setting)
    {
        ThrowUnlessEchoVolume(setting);
        return _baseEchoVolumes[(int)setting];
    }

    // Sets one of the two echo volumes as the song starts at it, held to its range.
    public void SetBaseEchoVolume(EchoSetting setting, int value)
    {
        ThrowUnlessEchoVolume(setting);
        _baseEchoVolumes[(int)setting] = EchoSettingRange(setting).Clamp(value);
    }

    // What one echo setting accepts in this song, the filter's range being the song's own table.
    public ValueRange EchoSettingRange(EchoSetting setting)
    {
        return setting == EchoSetting.Filter ? FilterRange : SnesEcho.Range(setting);
    }

    // One of the two echo volumes' changes, in tick order and one to a tick.
    public IReadOnlyList<LaneDivider<int>> EchoChanges(EchoSetting setting)
    {
        ThrowUnlessEchoVolume(setting);
        return _echoVolumeChanges[(int)setting];
    }

    // Puts one divider on the echo volume lane, both sides together, its values at tick 0 being the bases'.
    public void SetEchoVolumeChange(int tick, int left, int right, bool fades, int fadeToLeft, int fadeToRight)
    {
        PutEchoChange(EchoSetting.VolumeLeft, tick, left, fades, fadeToLeft);
        PutEchoChange(EchoSetting.VolumeRight, tick, right, fades, fadeToRight);
    }

    // Writes the whole echo volume lane from two sides' dividers, merged into one divider at every tick either side has one, a side with none there stating where it had got to.
    public void SetEchoVolumeChanges(IEnumerable<LaneDivider<int>> left, IEnumerable<LaneDivider<int>> right)
    {
        var leftByTick = left.ToDictionary(change => change.Tick);
        var rightByTick = right.ToDictionary(change => change.Tick);
        _echoVolumeChanges[(int)EchoSetting.VolumeLeft].Clear();
        _echoVolumeChanges[(int)EchoSetting.VolumeRight].Clear();

        // Where each side has got to by the end of the divider before.
        int reachedLeft = BaseEchoVolume(EchoSetting.VolumeLeft);
        int reachedRight = BaseEchoVolume(EchoSetting.VolumeRight);
        foreach (int tick in leftByTick.Keys.Concat(rightByTick.Keys).Distinct().Order())
        {
            bool hasLeft = leftByTick.TryGetValue(tick, out var leftChange);
            bool hasRight = rightByTick.TryGetValue(tick, out var rightChange);
            int valueLeft = hasLeft ? leftChange.Value : reachedLeft;
            int valueRight = hasRight ? rightChange.Value : reachedRight;
            bool fades = (hasLeft && leftChange.Fades) || (hasRight && rightChange.Fades);
            int fadeToLeft = hasLeft && leftChange.Fades ? leftChange.FadeTo : valueLeft;
            int fadeToRight = hasRight && rightChange.Fades ? rightChange.FadeTo : valueRight;
            SetEchoVolumeChange(tick, valueLeft, valueRight, fades, fadeToLeft, fadeToRight);
            reachedLeft = fades ? fadeToLeft : valueLeft;
            reachedRight = fades ? fadeToRight : valueRight;
        }
    }

    // Removes the echo volume divider at a tick, both sides together, and answers whether there was one.
    public bool RemoveEchoVolumeChange(int tick)
    {
        bool isLeftRemoved = RemoveChange(_echoVolumeChanges[(int)EchoSetting.VolumeLeft], tick);
        return RemoveChange(_echoVolumeChanges[(int)EchoSetting.VolumeRight], tick) || isLeftRemoved;
    }

    // One of the two echo volumes' changes at a tick, or null where there is none.
    public LaneDivider<int>? EchoChangeAt(EchoSetting setting, int tick)
    {
        ThrowUnlessEchoVolume(setting);
        return ChangeAt(_echoVolumeChanges[(int)setting], tick);
    }

    // The run covering a tick, or null where the echo is not running there.
    public EchoRun? EchoRunAt(int tick)
    {
        return LaneUnits.UnitCovering(_echoRuns, tick);
    }

    // The echo unit's settings in force at a tick, or the driver's own where no run is.
    public EchoUnitState EchoUnitStateAt(int tick)
    {
        return EchoRunAt(tick)?.State ?? EchoUnitState.Default;
    }

    // Puts one echo run down, cutting it out of any it overlaps.
    public void PlaceEchoRun(EchoRun run)
    {
        EditEchoRun([], [run]);
    }

    // Takes away the runs starting at the given ticks and puts the given ones down, each held to the song's ranges, and answers whether anything was asked.
    public bool EditEchoRun(IReadOnlyList<int> removedStartTicks, IReadOnlyList<EchoRun> placedRuns)
    {
        return EditUnits(_echoRuns, removedStartTicks, [.. placedRuns.Select(ClampedToSongRanges)]);
    }

    // Replaces the echo runs as given.
    public void SetEchoRuns(IReadOnlyList<EchoRun> runs)
    {
        ReplaceContents(_echoRuns, runs);
    }

    // The coefficients a filter index names, packed, and the default filter's for an index outside the table.
    public long FilterCoefficients(int index)
    {
        return index < 0 || index >= _filterTable.Count ? _filterTable[SnesEcho.DefaultFilter] : _filterTable[index];
    }

    // The filter index naming a set of coefficients, appending them to the table where none does yet.
    public int FilterIndexFor(long coefficients)
    {
        int filterIndex = _filterTable.IndexOf(coefficients);
        if (filterIndex >= 0)
        {
            return filterIndex;
        }

        _filterTable.Add(coefficients);
        return _filterTable.Count - 1;
    }

    // Drops every filter of the song's own that no run names and renumbers the runs onto what is left.
    public void CompactFilters()
    {
        // The filters in their new order: the presets, then the song's own in the order the runs first name them.
        var usedFilters = new List<long>(SnesEcho.PackedPresets);
        foreach (var run in _echoRuns)
        {
            long coefficients = FilterCoefficients(run.State.Filter);
            if (!usedFilters.Contains(coefficients))
            {
                usedFilters.Add(coefficients);
            }
        }

        if (usedFilters.SequenceEqual(_filterTable))
        {
            return;
        }

        for (int runIndex = 0; runIndex < _echoRuns.Count; runIndex++)
        {
            var run = _echoRuns[runIndex];
            _echoRuns[runIndex] = run with { State = run.State with { Filter = usedFilters.IndexOf(FilterCoefficients(run.State.Filter)) } };
        }

        ReplaceContents(_filterTable, usedFilters);
    }

    // Replaces the filter table as given, the driver's presets always first whatever it holds there.
    public void SetFilterTable(IReadOnlyList<long> filterTable)
    {
        ReplaceContents(_filterTable, SnesEcho.PackedPresets);
        foreach (long filter in filterTable.Skip(SnesEcho.PresetCount))
        {
            if (!_filterTable.Contains(filter))
            {
                _filterTable.Add(filter);
            }
        }
    }

    // Whether a channel is fed into the echo from tick 0.
    public bool IsBaseChannelEchoOn(int channel)
    {
        return _isBaseChannelEchoOn[channel];
    }

    // Sets whether a channel is fed into the echo from tick 0.
    public void SetBaseChannelEchoOn(int channel, bool isEchoOn)
    {
        _isBaseChannelEchoOn[channel] = isEchoOn;
    }

    // A channel's echo send changes in tick order, one to a tick, none at tick 0.
    public IReadOnlyList<LaneValueChange> ChannelEchoChanges(int channel)
    {
        return _channelEchoChanges[channel];
    }

    // Puts a channel echo send change at a tick, replacing whatever was there, its value the bit's own.
    public void SetChannelEchoChange(int channel, int tick, int value)
    {
        ThrowUnlessChangeTick(tick);
        PutChange(_channelEchoChanges[channel], new LaneValueChange(tick, Math.Clamp(value, ChannelEchoOff, ChannelEchoOn)));
    }

    // A channel's echo send as the stretches it is on over, derived from its base and its changes.
    public IReadOnlyList<EchoSendUnit> ChannelEchoUnits(int channel)
    {
        return EchoSendRuns.UnitsFromDividers(IsBaseChannelEchoOn(channel), ChannelEchoChanges(channel), RollLengthTicks);
    }

    // Writes a channel's echo send from the stretches it is on over, replacing its base and its changes.
    public void SetChannelEchoUnits(int channel, IReadOnlyList<EchoSendUnit> units)
    {
        int previousRollTicks = RollLengthTicks;
        var (isBaseOn, changes) = EchoSendRuns.DividersFromUnits(units, RollLengthTicks);
        _isBaseChannelEchoOn[channel] = isBaseOn;
        ReplaceContents(_channelEchoChanges[channel], changes);
        FollowRoll(previousRollTicks);
    }

    // Takes away a channel's echo send stretches starting at the given ticks and puts the given ones down, and answers whether anything was asked.
    public bool EditChannelEcho(int channel, IReadOnlyList<int> removedStartTicks, IReadOnlyList<EchoSendUnit> placedUnits)
    {
        var units = ChannelEchoUnits(channel).ToList();
        if (!LaneUnits.RemoveAndPlace(units, removedStartTicks, placedUnits))
        {
            return false;
        }

        SetChannelEchoUnits(channel, units);
        return true;
    }

    // Replaces the two echo volumes' bases and changes, every channel's echo send and the filter table as given.
    public void SetEchoState(
        IReadOnlyList<int> baseValues,
        IReadOnlyList<IReadOnlyList<LaneDivider<int>>> changes,
        IReadOnlyList<bool> isBaseChannelEchoOn,
        IReadOnlyList<IReadOnlyList<LaneValueChange>> channelChanges,
        IReadOnlyList<long> filterTable)
    {
        SetFilterTable(filterTable);
        foreach (var setting in SnesEcho.VolumeSettings)
        {
            int settingIndex = (int)setting;
            _baseEchoVolumes[settingIndex] = baseValues[settingIndex];
            ReplaceContents(_echoVolumeChanges[settingIndex], changes[settingIndex]);
        }

        for (int channel = 0; channel < ChannelCount; channel++)
        {
            _isBaseChannelEchoOn[channel] = isBaseChannelEchoOn[channel];
            ReplaceContents(_channelEchoChanges[channel], channelChanges[channel]);
        }
    }

    // Puts one of the two echo volumes' changes into its list, its values held to the volume's range and its value at tick 0 the base's.
    private void PutEchoChange(EchoSetting setting, int tick, int value, bool fades, int fadeTo)
    {
        var (changeValue, changeFadeTo) = FadingChangeAsPut(tick, value, fades, fadeTo, BaseEchoVolume(setting), EchoSettingRange(setting));
        PutChange(_echoVolumeChanges[(int)setting], new LaneDivider<int>(tick, changeValue, fades, changeFadeTo));
    }

    // A run whose three settings are held to the ranges this song takes.
    private EchoRun ClampedToSongRanges(EchoRun run)
    {
        var clampedState = new EchoUnitState(
            SnesEcho.DelayRange.Clamp(run.State.Delay),
            SnesEcho.FeedbackRange.Clamp(run.State.Feedback),
            FilterRange.Clamp(run.State.Filter));
        return run with { State = clampedState };
    }

    // Throws where a setting is not one of the two echo volumes.
    private static void ThrowUnlessEchoVolume(EchoSetting setting)
    {
        if (!IsEchoVolume(setting))
        {
            throw new ArgumentException(NotEchoVolumeMessage, nameof(setting));
        }
    }
}
