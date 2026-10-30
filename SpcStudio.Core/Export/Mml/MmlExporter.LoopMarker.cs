using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the loop marker: where it goes, and what is written in front of it and behind it.
public static partial class MmlExporter
{
    private const int NoLoopMarker = -1;

    // The tick every written channel puts its loop marker at, or the value saying there is none where the song writes none.
    public static int LoopMarkerTick(Song song)
    {
        if (!song.IsLooping)
        {
            return NoLoopMarker;
        }

        if (song.LoopTick > 0)
        {
            return song.LoopTick;
        }

        return DoesNeedStartMarker(song) ? 0 : NoLoopMarker;
    }

    // Whether a song looping from its first tick has to write its loop marker anyway: where it starts off a toggle's own state, or its end leaves a
    // toggle, the key-off remote slot or a transpose other than the one its start is under.
    private static bool DoesNeedStartMarker(Song song)
    {
        var staccato = ChangeMap.ForStaccato(song);
        int baseStaccato = staccato.InForceAt(0);
        int lastTick = SongLastTick(song);

        if (baseStaccato != SnesTiming.DefaultStaccato || staccato.InForceAt(lastTick) != baseStaccato)
        {
            return true;
        }

        for (int channel = 0; channel < Song.DriverChannelCount; channel++)
        {
            if (!WritesChannel(song, channel))
            {
                continue;
            }

            var legato = LegatoToggles.For(song, channel, HeldNoteShape.OneTickRest);
            if (legato.DoesHeaderToggle || legato.IsBitSetAt(lastTick) || TransposeLeftAtEnd(song, channel) != 0)
            {
                return true;
            }

            var hold = HoldToggles.For(song, channel, HeldNoteShape.OneTickRest);
            if (hold.IsArmedAt(lastTick) && !hold.DoesHeaderArm)
            {
                return true;
            }
        }

        return false;
    }

    // What a channel writes around the loop marker - its prelude just before it and its restatements just after it - or nothing where the song writes no marker.
    private static (IReadOnlyList<TimedCommand> Prelude, IReadOnlyList<TimedCommand> Restatements) LoopMarkerCommands(Song song, int channel, HeldNoteShape shape)
    {
        if (LoopMarkerTick(song) == NoLoopMarker)
        {
            return ([], []);
        }

        return (LoopPrelude(song, channel, shape), LoopRestatements(song, channel, shape));
    }

    // The toggles a channel writes just before the loop marker, which run on the first pass alone, where the state the song ends in differs from the one it reaches the marker in.
    private static IReadOnlyList<TimedCommand> LoopPrelude(Song song, int channel, HeldNoteShape shape)
    {
        return [.. TogglesWhereEndDiffers(song, channel, LegatoToggles.For(song, channel, shape), song.LoopTick - 1)];
    }

    // The driver state a channel writes again just after the loop marker, wherever what the song's end leaves differs from what the loop starts under.
    private static IReadOnlyList<TimedCommand> LoopRestatements(Song song, int channel, HeldNoteShape shape)
    {
        int loopTick = song.LoopTick;
        int lastTick = SongLastTick(song);
        var restatements = new List<TimedCommand>();

        if (channel == CommandChannel)
        {
            var tempo = TempoMap.For(song);
            restatements.AddRange(LetteredRestatement(song, 't', LaneRegions.TempoCommands(song), tick => tempo.TempoAt(tick)));

            var masterVolume = ChangeMap.ForMasterVolume(song);
            restatements.AddRange(LetteredRestatement(song, 'w', LaneRegions.MasterVolumeCommands(song), tick => masterVolume.InForceAt(tick)));
        }

        var channelVolume = ChangeMap.ForChannelVolume(song, channel);
        restatements.AddRange(LetteredRestatement(song, 'v', LaneRegions.ChannelVolumeCommands(song, channel), tick => channelVolume.InForceAt(tick)));

        var pan = ChangeMap.ForChannelPan(song, channel);
        var surround = ChangeMap.ForChannelSurround(song, channel);
        restatements.AddRange(RegionRestatement(
            song,
            LaneRegions.PanCommands(song, channel).Select(written => written.Command),
            tick => (Pan: pan.InForceAt(tick), Surround: surround.InForceAt(tick)),
            atLoop => LetteredCommand(loopTick, 'y', atLoop.Pan, SnesSurround.FromPacked(atLoop.Surround))));

        var amplify = ChangeMap.ForChannelAmplify(song, channel);
        if (IsRestatementNeeded(NoteAmplify.Lane.Merged(song, channel).Changes.Any(change => change.Tick == loopTick), amplify.InForceAt(loopTick), amplify.InForceAt(lastTick)))
        {
            restatements.Add(new TimedCommand(loopTick, AmplifyOrder, AmpName.Write(amplify.InForceAt(loopTick))));
        }

        restatements.AddRange(SweepRestatement(song, VibratoMap.For(song, channel), VibratoOrder, VibratoCommandFor));
        restatements.AddRange(SweepRestatement(song, TremoloMap.For(song, channel), TremoloOrder, TremoloCommandFor));

        if (channel == CommandChannel && song.IsEchoEnabled)
        {
            restatements.AddRange(EchoRestatements(song));
        }

        var legato = LegatoToggles.For(song, channel, shape);
        restatements.AddRange(TogglesWhereEndDiffers(song, channel, legato, loopTick));

        restatements.AddRange(ArmRestatement(HoldToggles.For(song, channel, shape), loopTick, lastTick, HoldOrder, HoldCommandFor));
        restatements.AddRange(ArmRestatement(legato.KeyOnArms, loopTick, lastTick, LegatoHoldOrder, LegatoHoldCommandFor));
        return restatements;
    }

    // The toggles written at the loop marker's tick where the state at a tick differs from the one the song's end leaves: the light staccato's on the
    // command channel, and the channel's own legato.
    private static IEnumerable<TimedCommand> TogglesWhereEndDiffers(Song song, int channel, LegatoToggleList legato, int comparedTick)
    {
        int lastTick = SongLastTick(song);

        if (channel == CommandChannel)
        {
            var staccato = ChangeMap.ForStaccato(song);

            if (staccato.InForceAt(comparedTick) != staccato.InForceAt(lastTick))
            {
                yield return new TimedCommand(song.LoopTick, StaccatoOrder, StaccatoName.Write());
            }
        }

        if (legato.IsBitSetAt(comparedTick) != legato.IsBitSetAt(lastTick))
        {
            yield return new TimedCommand(song.LoopTick, LegatoOrder, LegatoName.Write());
        }
    }

    // A lane with an MML letter drawn as regions written again just after the loop marker, or nothing where it needs no restatement.
    private static IEnumerable<TimedCommand> LetteredRestatement(Song song, char letter, IEnumerable<LaneCommand> commands, Func<int, int> valueAt)
    {
        return RegionRestatement(song, commands, valueAt, atLoop => LetteredCommand(song.LoopTick, letter, atLoop));
    }

    // A lane drawn as regions written again just after the loop marker under the value in force just before it, or nothing where it needs no restatement.
    private static IEnumerable<TimedCommand> RegionRestatement<TValue>(Song song, IEnumerable<LaneCommand> commands, Func<int, TValue> valueAt, Func<TValue, TimedCommand> commandFor)
    {
        var atLoop = valueAt(song.LoopTick - 1);

        if (IsRestatementNeeded(DoesStateRegionValueAt(commands, song.LoopTick), atLoop, valueAt(SongLastTick(song))))
        {
            yield return commandFor(atLoop);
        }
    }

    // The arm or the clear of a remote slot written again just after the loop marker, where the slot the loop starts under differs from the one the
    // song's end leaves and nothing at the marker's tick says it.
    private static IEnumerable<TimedCommand> ArmRestatement(HoldCommandList arms, int loopTick, int lastTick, int sortOrder, Func<bool, string> commandFor)
    {
        bool isArmedAtLoop = arms.IsArmedAt(loopTick);
        if (IsRestatementNeeded(arms.ArmAndClearTicks.Any(command => command.Tick == loopTick), isArmedAtLoop, arms.IsArmedAt(lastTick)))
        {
            yield return new TimedCommand(loopTick, sortOrder, commandFor(isArmedAtLoop));
        }
    }

    // The echo commands the command channel writes again just after the loop marker.
    private static IEnumerable<TimedCommand> EchoRestatements(Song song)
    {
        int loopTick = song.LoopTick;
        var echo = EchoMap.ForDriverChannels(song);
        var atLoop = echo.StateAt(loopTick);
        var atEnd = echo.StateAt(SongLastTick(song));

        if (atLoop.IsEnabled && !echo.SendTicks(song).Contains(loopTick) && !atLoop.WritesSameSendAs(atEnd))
        {
            yield return new TimedCommand(loopTick, EchoSendOrder, EchoSendCommand(atLoop));
        }

        if (!atLoop.IsEnabled && atEnd.IsEnabled)
        {
            yield return new TimedCommand(loopTick, EchoOffOrder, EchoOffName.Write());
        }

        bool isFilterDifferent = !atLoop.HasSameFilterAs(atEnd);
        bool isUnitRestated = atLoop.IsEnabled && !echo.SettingsTicks().Contains(loopTick) && (!atLoop.WritesSameUnitAs(atEnd) || (isFilterDifferent && !SnesEcho.IsCustomFilter(atLoop.Filter)));

        if (isUnitRestated)
        {
            yield return new TimedCommand(loopTick, EchoUnitOrder, EchoUnitCommand(atLoop));
        }

        if (!echo.FilterTicks().Contains(loopTick) && SnesEcho.IsCustomFilter(atLoop.Filter) && (isFilterDifferent || isUnitRestated))
        {
            yield return new TimedCommand(loopTick, EchoFilterOrder, EchoFilterCommand(atLoop));
        }
    }

    // One of a channel's sweeps written again just after the loop marker, or nothing where it needs no restatement.
    private static IEnumerable<TimedCommand> SweepRestatement<TState>(Song song, SweepMap<TState> sweep, int sortOrder, Func<TState, string> commandFor) where TState : struct, ISweepState<TState>
    {
        var atLoop = sweep.StateAt(song.LoopTick);

        if (IsRestatementNeeded(sweep.StateChangeTicks().Contains(song.LoopTick), atLoop, sweep.StateAt(SongLastTick(song))))
        {
            yield return new TimedCommand(song.LoopTick, sortOrder, commandFor(atLoop));
        }
    }

    // The song's last tick, whose state is what the second pass reaches the loop marker under.
    private static int SongLastTick(Song song)
    {
        return song.EndTick - 1;
    }

    // Whether a value has to be written again just after the loop marker: nothing at the marker states it already, and the song's end leaves another.
    private static bool IsRestatementNeeded<T>(bool isStatedAtMarker, T valueLoopStartsUnder, T valueEndLeaves)
    {
        return !isStatedAtMarker && !EqualityComparer<T>.Default.Equals(valueLoopStartsUnder, valueEndLeaves);
    }

    // Whether a command of a lane drawn as regions at the tick sets the lane's value.
    private static bool DoesStateRegionValueAt(IEnumerable<LaneCommand> commands, int tick)
    {
        return commands.Any(command => command.Tick == tick && command.SetValue is not null);
    }

    // The $FA $02 shift a channel's last written note leaves, which the second pass reaches the loop marker under.
    private static int TransposeLeftAtEnd(Song song, int channel)
    {
        return PlayedNotes(song, channel).Aggregate(0, (transpose, note) => TransposeFor(note.Semitone, transpose));
    }

    // The fine tune a channel's last written note leaves, which the second pass reaches the loop marker under.
    private static int FineTuneLeftAtEnd(Song song, int channel)
    {
        return PlayedNotes(song, channel).Select(note => note.FineTune).DefaultIfEmpty(SnesPitch.MinFineTune).Last();
    }
}
