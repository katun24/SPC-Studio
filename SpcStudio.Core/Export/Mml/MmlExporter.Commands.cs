using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the commands a channel writes between its notes, each in a fixed order within a tick.
public static partial class MmlExporter
{
    private const int EchoOffOrder = 'y' + 1;
    private const int EchoSendOrder = EchoOffOrder + 1;
    private const int EchoFadeOrder = EchoSendOrder + 1;
    private const int EchoUnitOrder = EchoFadeOrder + 1;
    private const int EchoFilterOrder = EchoUnitOrder + 1;
    private const int StaccatoOrder = EchoFilterOrder + 1;
    private const int VibratoOrder = StaccatoOrder + 1;
    private const int TremoloOrder = VibratoOrder + 1;
    private const int LegatoOrder = TremoloOrder + 1;
    private const int HoldOrder = LegatoOrder + 1;
    private const int LegatoHoldOrder = HoldOrder + 1;
    private const int AmplifyOrder = LegatoHoldOrder + 1;
    private const int EnvelopeOrder = AmplifyOrder + 1;
    private const int EnvelopeRestoreOrder = EnvelopeOrder + 1;

    // The commands a channel writes between its notes after its header, in tick order and in a fixed order within a tick: its own lanes always, and the song-wide ones on the command channel alone.
    private static IEnumerable<TimedCommand> ChannelCommands(Song song, int channel, HeldNoteShape shape)
    {
        int markerTick = LoopMarkerTick(song);
        var legato = LegatoToggles.For(song, channel, shape);
        var commands = LaneRegions.ChannelVolumeCommands(song, channel)
            .SelectMany(command => RegionCommands('v', command))
            .Concat(LaneRegions.PanCommands(song, channel).SelectMany(written => RegionCommands('y', written.Command, written.Surround)))
            .Concat(NoteAmplify.Lane.Merged(song, channel).Changes.Select(change => new TimedCommand(change.Tick, AmplifyOrder, AmpName.Write(change.Value))))
            .Concat(SweepCommands(VibratoMap.For(song, channel), VibratoOrder, VibratoCommandFor))
            .Concat(SweepCommands(TremoloMap.For(song, channel), TremoloOrder, TremoloCommandFor))
            .Concat(EnvelopeCommands(song, channel))
            .Concat(LegatoToggles.ToggleTicks(song, channel, markerTick, shape).Select(tick => new TimedCommand(tick, LegatoOrder, LegatoName.Write())))
            .Concat(HoldToggles.For(song, channel, shape).ArmAndClearTicks.Select(toggle => new TimedCommand(toggle.Tick, HoldOrder, HoldCommandFor(toggle.Arms))))
            .Concat(legato.KeyOnArms.ArmAndClearTicks.Select(toggle => new TimedCommand(toggle.Tick, LegatoHoldOrder, LegatoHoldCommandFor(toggle.Arms))));

        if (channel == CommandChannel)
        {
            commands = commands
                .Concat(LaneRegions.TempoCommands(song).SelectMany(command => RegionCommands('t', command)))
                .Concat(LaneRegions.MasterVolumeCommands(song).SelectMany(command => RegionCommands('w', command)))
                .Concat(EchoCommands(song))
                .Concat(StaccatoToggleTicks(song, markerTick).Select(tick => new TimedCommand(tick, StaccatoOrder, StaccatoName.Write())));
        }

        return commands.Where(command => command.Tick < song.EndTick).OrderBy(command => command.Tick).ThenBy(command => command.SortOrder);
    }

    // The channel header's y: its base pan, with its base surround pair where it inverts either side.
    private static string PanHeader(Song song, int channel)
    {
        var start = LaneRegions.PanWritten(song, channel).Base;
        return LetteredText('y', start.Value, start.Surround);
    }

    // One command of a lane that has an MML letter, with the surround pair behind the value where it inverts either side.
    private static TimedCommand LetteredCommand(int tick, char letter, int value, SnesSurround surround = default)
    {
        return new TimedCommand(tick, letter, LetteredText(letter, value, surround));
    }

    // A command written as its letter and a value, with the surround pair behind the value where it inverts either side.
    private static string LetteredText(char letter, int value, SnesSurround surround = default)
    {
        return surround.IsAnyInverted ? $"{letter}{value},{(surround.IsLeftInverted ? 1 : 0)},{(surround.IsRightInverted ? 1 : 0)}" : $"{letter}{value}";
    }

    // The commands one region of a lane drawn as regions is written as: its set where it states a value, and its fade where it fades.
    private static IEnumerable<TimedCommand> RegionCommands(char letter, LaneCommand command, SnesSurround surround = default)
    {
        if (command.SetValue is int value)
        {
            yield return LetteredCommand(command.Tick, letter, value, surround);
        }

        if (command.Fades)
        {
            yield return new TimedCommand(command.Tick, letter, FadeCommandText(letter, command.FadeTicks, command.FadeTo));
        }
    }

    // The fade form of a lettered lane's command: a second argument for t, w and v, and a named command for the pan.
    private static string FadeCommandText(char letter, int fadeTicks, int fadeTo)
    {
        return letter == 'y' ? PanFadeName.Write(fadeTicks, fadeTo) : $"{letter}{fadeTicks},{fadeTo}";
    }

    // What a channel's header says about its amplify: the command where the channel starts under one, and nothing where it does not.
    private static string AmplifyHeader(Song song, int channel)
    {
        int baseAmplify = NoteAmplify.Lane.Merged(song, channel).Base;
        return baseAmplify == SnesVolume.DefaultAmplify ? "" : $"{AmpName.Write(baseAmplify)} ";
    }

    // The $EDs that put each envelope override unit's bytes up, at the unit's start and after every @ it covers, and the restores that end a unit.
    private static IEnumerable<TimedCommand> EnvelopeCommands(Song song, int channel)
    {
        var envelope = EnvelopeMap.For(song, channel);

        foreach (var (tick, from, to) in envelope.OverrideWrites())
        {
            foreach (var (first, second) in SnesEnvelopeOverride.CommandsBetween(from, to))
            {
                yield return new TimedCommand(tick, EnvelopeOrder, EnvName.Write(first, second));
            }
        }

        foreach (int tick in envelope.RestoreTicks())
        {
            yield return new TimedCommand(tick, EnvelopeRestoreOrder, EnvResetName.Write());
        }
    }

    // A command a channel writes between its notes, as its text and a key saying which of two at one tick comes first.
    private readonly record struct TimedCommand(int Tick, int SortOrder, string Text);
}
