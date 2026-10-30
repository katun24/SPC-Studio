namespace SpcStudio.Model;

// The song's echo resolved at a tick, and the ticks each of the driver's echo commands has to be written at.
public sealed class EchoMap
{
    private readonly IReadOnlyList<EchoRun> _runs;
    private readonly ChangeMap[] _volumes;
    private readonly ChangeMap[] _channelSends;
    private readonly bool[] _isChannelIncluded;
    private readonly long[] _filters;

    private EchoMap(IReadOnlyList<EchoRun> runs, ChangeMap[] volumes, ChangeMap[] channelSends, bool[] isChannelIncluded, long[] filters)
    {
        _runs = runs;
        _volumes = volumes;
        _channelSends = channelSends;
        _isChannelIncluded = isChannelIncluded;
        _filters = filters;
    }

    // Whether the song has echo anywhere at all.
    public bool IsEnabled
    {
        get
        {
            return _runs.Count > 0;
        }
    }

    // The song's echo map, leaving out the sends of every channel the optional test says to leave out.
    public static EchoMap For(Song song, Func<int, bool>? includesChannel = null)
    {
        return Of(song, channel => ChangeMap.ForChannelEcho(song, channel), includesChannel);
    }

    // The song's echo map with every channel's send included, the notes given standing in for one channel's own.
    public static EchoMap WithChannelNotes(Song song, int notesChannel, IReadOnlyList<Note> notes)
    {
        return Of(song, channel => ChangeMap.ForChannelEcho(song, channel, channel == notesChannel ? notes : null), null);
    }

    // The song's echo map as the driver plays it, leaving out the sends of every extra channel.
    public static EchoMap ForDriverChannels(Song song)
    {
        return For(song, channel => !Song.IsExtraChannel(channel));
    }

    // The whole echo at a tick: off where no run covers it, and otherwise that run's three settings with the mask and the two volumes their own lanes hold there.
    public EchoState StateAt(double tick)
    {
        if (LaneUnits.UnitCovering(_runs, (int)tick) is not { } run)
        {
            return EchoState.Off;
        }

        int channelMask = 0;
        for (int channel = 0; channel < _channelSends.Length; channel++)
        {
            if (_isChannelIncluded[channel] && _channelSends[channel].InForceAt(tick) != 0)
            {
                channelMask |= 1 << channel;
            }
        }

        return new EchoState(
            true,
            channelMask,
            _volumes[0].InForceAt(tick),
            _volumes[1].InForceAt(tick),
            run.State.Delay,
            run.State.Feedback,
            run.State.Filter,
            CoefficientsOfFilter(run.State.Filter));
    }

    // The ticks at which an $EF has to be written: where a stretch of echo begins, and where the mask or either volume moves while one is running.
    public IEnumerable<int> SendTicks(Song song)
    {
        var volumeSetTicks = LaneRegions.EchoVolumeCommands(song).Where(command => command.Sets).Select(command => command.Tick);

        // The ticks an included channel's echo bit changes at.
        var maskChangeTicks = Enumerable.Range(0, _isChannelIncluded.Length)
            .Where(channel => _isChannelIncluded[channel])
            .SelectMany(channel => NoteEchoSend.Merged(song, channel).Changes.Select(change => change.Tick));
        return EchoRuns.StartTicks(_runs)
            .Concat(TicksWhileEchoRuns(volumeSetTicks))
            .Concat(TicksWhileEchoRuns(maskChangeTicks))
            .Distinct()
            .Order();
    }

    // The ticks at which an $F2 has to be written, one per piece a long fade is cut into, leaving out a fade outside every run.
    public IEnumerable<int> VolumeFadeTicks(Song song)
    {
        var fadePieceStartTicks = LaneRegions.EchoVolumeCommands(song).Where(command => command.Fades).Select(command => command.Tick);
        return TicksWhileEchoRuns(fadePieceStartTicks).Distinct().Order();
    }

    // The $F2 starting at a tick, or null where none does.
    public EchoVolumeCommand? VolumeFadeAt(Song song, int tick)
    {
        return LaneRegions.EchoVolumeCommands(song)
            .Where(command => command.Fades && command.Tick == tick)
            .Select(command => (EchoVolumeCommand?)command)
            .FirstOrDefault();
    }

    // The ticks at which an $F1 has to be written: where a stretch begins, and where one run gives way to another with different settings to state.
    public IEnumerable<int> SettingsTicks()
    {
        return EchoRuns.SettingsTicks(_runs).Distinct().Order();
    }

    // The ticks at which an $F0 has to be written, which is the end of every stretch of echo.
    public IEnumerable<int> StopTicks()
    {
        return EchoRuns.StopTicks(_runs).Distinct().Order();
    }

    // The ticks at which an $F5 has to be written, which is every $F1 written while a filter of the song's own is in force as well as every move to one.
    public IEnumerable<int> FilterTicks()
    {
        return EchoRuns.FilterTicks(_runs).Distinct().Order();
    }

    // Every tick any of the echo commands has to be written at.
    public IEnumerable<int> AllTicks(Song song)
    {
        return SendTicks(song)
            .Concat(VolumeFadeTicks(song))
            .Concat(SettingsTicks())
            .Concat(StopTicks())
            .Concat(FilterTicks())
            .Distinct()
            .Order();
    }

    // The song's echo map with each channel's send taken from a map of it, leaving out the sends of every channel the optional test says to leave out.
    private static EchoMap Of(Song song, Func<int, ChangeMap> sendOfChannel, Func<int, bool>? includesChannel)
    {
        return new(
            song.EchoRuns,
            [ChangeMap.ForEcho(song, EchoSetting.VolumeLeft), ChangeMap.ForEcho(song, EchoSetting.VolumeRight)],
            [.. Enumerable.Range(0, song.ChannelCount).Select(sendOfChannel)],
            [.. Enumerable.Range(0, song.ChannelCount).Select(channel => includesChannel?.Invoke(channel) ?? true)],
            [.. song.FilterTable]);
    }

    // The filter coefficients a filter index stands for, falling back on the default preset where the index names nothing.
    private long CoefficientsOfFilter(int filter)
    {
        if (filter < 0 || filter >= _filters.Length)
        {
            return SnesEcho.PackedPresets[SnesEcho.DefaultFilter];
        }

        return _filters[filter];
    }

    // The ticks of these that fall inside a stretch of echo.
    private IEnumerable<int> TicksWhileEchoRuns(IEnumerable<int> ticks)
    {
        return ticks.Where(tick => LaneUnits.UnitCovering(_runs, tick) is not null);
    }
}
