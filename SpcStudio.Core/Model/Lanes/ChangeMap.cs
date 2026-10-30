namespace SpcStudio.Model;

// One driver byte as a function of position: a base from tick 0 and the commands laid over it, for the song volume, each channel's volume, pan,
// surround, amplify and echo bit, the echo volumes and the light staccato.
public sealed class ChangeMap
{
    private readonly Segment[] _segments;

    // The faded byte as each segment leaves it, at that segment's own tick.
    private readonly SnesFade[] _stateAfterSegment;

    // A map over its segments, the faded byte each one leaves worked out once.
    private ChangeMap(Segment[] segments)
    {
        _segments = segments;
        _stateAfterSegment = new SnesFade[segments.Length];
        var state = SnesFade.HeldAt(segments[0].Value ?? 0);
        int previousTick = 0;
        for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            state = state.Advanced(segment.Tick - previousTick);
            previousTick = segment.Tick;
            if (segment.Value is int setValue)
            {
                state = state.WithValueSet(setValue);
            }

            if (SnesFade.DoesFade(segment.FadeTicks))
            {
                state = state.FadingTo(segment.FadeTo, segment.FadeTicks);
            }

            _stateAfterSegment[segmentIndex] = state;
        }
    }

    // The song's song volume, MML w, with its $E1 fades.
    public static ChangeMap ForMasterVolume(Song song)
    {
        return FromCommands(song.BaseMasterVolume, LaneRegions.MasterVolumeCommands(song));
    }

    // One channel's own volume, MML v, with its $E8 fades and each note's own laid over the lane, the optional notes standing in for the channel's.
    public static ChangeMap ForChannelVolume(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var written = LaneRegions.ChannelVolumeWritten(song, channel, notes);
        return FromCommands(written.Base, written.Commands);
    }

    // One channel's volume as the lane alone says it, with no note's override laid over it.
    public static ChangeMap ForChannelVolumeOwn(Song song, int channel)
    {
        return ForRegions(song.BaseChannelVolume(channel), LaneRegions.ChannelVolumeOwn(song, channel));
    }

    // Any region lane from its base and its regions, played as the driver commands the regions are written as.
    public static ChangeMap ForRegions(int baseValue, IReadOnlyList<LaneRegion> regions)
    {
        return FromCommands(baseValue, LaneRegions.Commands(regions));
    }

    // One channel's pan, MML y, with its $DC fades and each note's own laid over the lane, the optional notes standing in for the channel's.
    public static ChangeMap ForChannelPan(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var written = LaneRegions.PanWritten(song, channel, notes);
        return FromCommands(written.Base.Value, written.Commands.Select(command => command.Command));
    }

    // One channel's surround sound, y's second and third arguments, packed, the optional notes standing in for the channel's.
    public static ChangeMap ForChannelSurround(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var written = LaneRegions.PanWritten(song, channel, notes);

        // The surround each pan command that sets a value states, at its tick.
        var surroundChanges = written.Commands
            .Where(command => command.Command.SetValue is not null)
            .Select(command => new LaneValueChange(command.Command.Tick, command.Surround.Packed));
        return FromSets(written.Base.Surround.Packed, surroundChanges);
    }

    // One channel's pan as the lane alone says it, with no note's override laid over it.
    public static ChangeMap ForChannelPanOwn(Song song, int channel)
    {
        return FromCommands(song.BasePan(channel), LaneRegions.PanOwnCommands(song, channel).Select(written => written.Command));
    }

    // One channel's surround sound as the lane alone says it, with no note's override laid over it.
    public static ChangeMap ForChannelSurroundOwn(Song song, int channel)
    {
        // The surround each of the lane's pan commands that sets a value states, at its tick.
        var surroundChanges = LaneRegions.PanOwnCommands(song, channel)
            .Where(written => written.Command.SetValue is not null)
            .Select(written => new LaneValueChange(written.Command.Tick, written.Surround.Packed));
        return FromSets(song.BaseSurround(channel).Packed, surroundChanges);
    }

    // One channel's amplify, $FA $03, with each note's own laid over the lane, the optional notes standing in for the channel's.
    public static ChangeMap ForChannelAmplify(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var (baseValue, changes) = NoteAmplify.Lane.Merged(song, channel, notes);
        return FromSets(baseValue, changes);
    }

    // One channel's amplify as the lane alone says it.
    public static ChangeMap ForChannelAmplifyOwn(Song song, int channel)
    {
        return FromSets(song.BaseAmplify(channel), song.AmplifyChanges(channel));
    }

    // The song's light staccato, the driver's wait time, derived from the lane's units.
    public static ChangeMap ForStaccato(Song song)
    {
        return FromSets(StaccatoRuns.StateAt(song.StaccatoUnits, 0), StaccatoRuns.StateChanges(song.StaccatoUnits).Where(change => change.Tick > 0));
    }

    // One of the song's two echo volumes, with its $F2 fades.
    public static ChangeMap ForEcho(Song song, EchoSetting setting)
    {
        bool isRight = setting == EchoSetting.VolumeRight;

        // The echo volume commands as they set and fade the one side.
        var sideCommands = LaneRegions.EchoVolumeCommands(song).Select(command => new LaneCommand(
            command.Tick,
            command.Sets ? (isRight ? command.Right : command.Left) : null,
            command.FadeTicks,
            isRight ? command.FadeToRight : command.FadeToLeft));
        return FromCommands(song.BaseEchoVolume(setting), sideCommands);
    }

    // One channel's echo bit, its place in $EF's mask, with each note's own laid over the lane, the optional notes standing in for the channel's.
    public static ChangeMap ForChannelEcho(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var (baseValue, changes) = NoteEchoSend.Lane.Merged(song, channel, notes);
        return FromSets(baseValue, changes);
    }

    // One channel's echo bit as the lane alone says it.
    public static ChangeMap ForChannelEchoOwn(Song song, int channel)
    {
        return FromSets(song.IsBaseChannelEchoOn(channel) ? 1 : 0, song.ChannelEchoChanges(channel));
    }

    // A map with no changes, which is one segment at the value.
    public static ChangeMap Flat(int value)
    {
        return FromSets(value, []);
    }

    // The value in force at a tick, which is what a change placed there would start at.
    public int InForceAt(double tick)
    {
        return StateAt(tick).Value;
    }

    // The whole faded byte at a tick, the value and any fade still running through it.
    public SnesFade StateAt(double tick)
    {
        int wholeTick = (int)Math.Floor(tick);
        int lastSegmentIndex = LastSegmentIndexAt(wholeTick);
        if (lastSegmentIndex < 0)
        {
            return SnesFade.HeldAt(_segments[0].Value ?? 0);
        }

        return _stateAfterSegment[lastSegmentIndex].Advanced(wholeTick - _segments[lastSegmentIndex].Tick + 1);
    }

    // The ticks of the commands strictly inside a span, which a note covering it has to be broken at.
    public IEnumerable<int> ChangesWithin(int startTick, int endTick)
    {
        foreach (var segment in _segments)
        {
            if (segment.Tick > startTick && segment.Tick < endTick)
            {
                yield return segment.Tick;
            }
        }
    }

    // A map of a lane whose command only ever sets the byte, a set at tick 0 being left to the base.
    private static ChangeMap FromSets(int baseValue, IEnumerable<LaneValueChange> changes)
    {
        // The segments, opening on the base at tick 0.
        var segments = new List<Segment> { new(0, baseValue, SnesFade.NoFade, 0) };
        foreach (var change in changes)
        {
            if (change.Tick != 0)
            {
                segments.Add(new Segment(change.Tick, change.Value, SnesFade.NoFade, 0));
            }
        }

        return new ChangeMap([.. segments]);
    }

    // A map of a lane drawn as regions, from the driver commands its regions are written as, a command at tick 0 lending the base its fade alone.
    private static ChangeMap FromCommands(int baseValue, IEnumerable<LaneCommand> commands)
    {
        // The segments, opening on the base at tick 0.
        var segments = new List<Segment> { new(0, baseValue, SnesFade.NoFade, 0) };
        foreach (var command in commands)
        {
            if (command.Tick == 0)
            {
                segments[0] = segments[0] with { FadeTicks = command.FadeTicks, FadeTo = command.FadeTo };
            }
            else
            {
                segments.Add(new Segment(command.Tick, command.SetValue, command.FadeTicks, command.FadeTo));
            }
        }

        return new ChangeMap([.. segments]);
    }

    // The index of the last segment at or before a tick, or -1 where there is none.
    private int LastSegmentIndexAt(int tick)
    {
        return SortedSearch.CountLeading(_segments, segment => segment.Tick <= tick) - 1;
    }

    // One command's tick, the value it sets or null where it only arms a fade, and the fade it arms.
    private readonly record struct Segment(int Tick, int? Value, int FadeTicks, int FadeTo);
}
