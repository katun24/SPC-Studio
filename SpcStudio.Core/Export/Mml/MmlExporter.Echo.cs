using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the echo's commands: the ones above the channels and the ones the command channel writes.
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

    // The echo commands the command channel writes after its header, each where what it states changes.
    private static IEnumerable<TimedCommand> EchoCommands(Song song)
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

        foreach (int tick in echo.SendTicks(song).Where(IsAfterHeader))
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
