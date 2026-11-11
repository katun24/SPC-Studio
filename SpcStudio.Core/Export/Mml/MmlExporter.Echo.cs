using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the echo's commands: the ones above the channels, the ones after them and the $F4 $03s that
// stand in for some of those.
public static partial class MmlExporter
{
    // What the song says about the echo above its channels: $F0 where it does not start with the echo running, and otherwise the echo it starts with.
    private static IEnumerable<string> EchoHeader(Song song)
    {
        if (song.EchoRunAt(0) is null)
        {
            yield return EchoOffName.Write();
            yield break;
        }

        var echo = EchoMap.ForDriverChannels(song);
        var state = echo.StateAt(0);

        yield return EchoSendCommand(state);
        yield return EchoUnitCommand(state);
        if (SnesEcho.IsCustomFilter(state.Filter))
        {
            yield return EchoFilterCommand(state);
        }

        if (echo.VolumeFadeAt(song, 0) is EchoVolumeCommand openingFade)
        {
            yield return EchoFadeCommand(openingFade);
        }
    }

    // The song's echo commands after its header, each where what it states changes, no $EF written at the ticks the channels toggle their bits at instead.
    private static IEnumerable<TimedCommand> EchoCommands(Song song, IReadOnlySet<int> toggledSendTicks)
    {
        if (!song.IsEchoEnabled)
        {
            yield break;
        }

        var echo = EchoMap.ForDriverChannels(song);

        foreach (int tick in echo.StopTicks().Where(IsAfterHeader))
        {
            yield return new TimedCommand(tick, EchoOffOrder, EchoOffName.Write());
        }

        foreach (int tick in echo.SendTicks(song).Where(tick => IsAfterHeader(tick) && !toggledSendTicks.Contains(tick)))
        {
            yield return new TimedCommand(tick, EchoSendOrder, EchoSendCommand(echo.StateAt(tick)));
        }

        foreach (int tick in echo.VolumeFadeTicks(song).Where(IsAfterHeader))
        {
            if (echo.VolumeFadeAt(song, tick) is EchoVolumeCommand fade)
            {
                yield return new TimedCommand(tick, EchoFadeOrder, EchoFadeCommand(fade));
            }
        }

        foreach (int tick in echo.SettingsTicks().Where(IsAfterHeader))
        {
            yield return new TimedCommand(tick, EchoUnitOrder, EchoUnitCommand(echo.StateAt(tick)));
        }

        foreach (int tick in echo.FilterTicks().Where(IsAfterHeader))
        {
            yield return new TimedCommand(tick, EchoFilterOrder, EchoFilterCommand(echo.StateAt(tick)));
        }
    }

    // The channels whose echo bit an $F4 $03 moves at each tick an $EF would be written at otherwise: inside the song, away from the loop marker's tick,
    // the echo running on both sides of it, only bits moving and every channel whose bit moves written, with no echo volume command there or fading
    // before it.
    private static Dictionary<int, List<int>> EchoToggledChannelsByTick(Song song)
    {
        var toggledChannelsByTick = new Dictionary<int, List<int>>();
        if (!song.IsEchoEnabled)
        {
            return toggledChannelsByTick;
        }

        var echo = EchoMap.ForDriverChannels(song);
        var volumeCommands = LaneRegions.EchoVolumeCommands(song).ToList();
        int markerTick = LoopMarkerTick(song);
        foreach (int tick in echo.SendTicks(song).Where(tick => IsAfterHeader(tick) && tick != markerTick && tick < song.EndTick))
        {
            var stateBefore = echo.StateAt(tick - 1);
            var stateAt = echo.StateAt(tick);
            int movedBits = stateBefore.ChannelMask ^ stateAt.ChannelMask;
            bool doVolumesStay = stateBefore.VolumeLeft == stateAt.VolumeLeft && stateBefore.VolumeRight == stateAt.VolumeRight && !IsEchoVolumeCommandedAt(volumeCommands, tick);
            if (!stateBefore.IsEnabled || !stateAt.IsEnabled || movedBits == 0 || !doVolumesStay)
            {
                continue;
            }

            var movedChannels = Enumerable.Range(0, Song.DriverChannelCount).Where(channel => SnesDspRegister.HasVoiceBit(movedBits, channel)).ToList();
            if (movedChannels.All(channel => WritesChannel(song, channel)))
            {
                toggledChannelsByTick[tick] = movedChannels;
            }
        }

        return toggledChannelsByTick;
    }

    // Whether an echo volume command stands at the tick, or the latest one before it fades, either of which leaves the volumes for an $EF to state.
    private static bool IsEchoVolumeCommandedAt(List<EchoVolumeCommand> volumeCommands, int tick)
    {
        var latestCommand = volumeCommands.Where(command => command.Tick <= tick).Select(command => (EchoVolumeCommand?)command).LastOrDefault();
        return latestCommand is { } command && (command.Tick == tick || command.Fades);
    }

    // Whether a tick is past the one the header states.
    private static bool IsAfterHeader(int tick)
    {
        return tick > 0;
    }

    // $EF: which channels feed the echo buffer, and the two echo volumes.
    private static string EchoSendCommand(EchoState state)
    {
        return EchoName.Write(state.ChannelMask, state.VolumeLeft, state.VolumeRight);
    }

    // $F1: the delay, the feedback and the FIR preset the driver loads.
    private static string EchoUnitCommand(EchoState state)
    {
        return EchoSetupName.Write(state.Delay, state.Feedback, SnesEcho.PresetByteFor(state.Filter));
    }

    // $F5: the FIR coefficients of a filter that is the song's own.
    private static string EchoFilterCommand(EchoState state)
    {
        return FirName.Write([.. SnesEcho.UnpackCoefficients(state.PackedCoefficients).Select(coefficient => (int)(byte)coefficient)]);
    }

    // $F2: one duration and a target for each side.
    private static string EchoFadeCommand(EchoVolumeCommand command)
    {
        return EchoFadeName.Write(command.FadeTicks, command.FadeToLeft, command.FadeToRight);
    }
}
