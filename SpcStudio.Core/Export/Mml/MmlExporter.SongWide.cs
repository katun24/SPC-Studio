using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the song-wide commands after the header - the tempo, the song volume, the echo and the light
// staccato - the channel each tick of them is written in, and the $F4 $03s the channels write in place of some of the echo's.
public static partial class MmlExporter
{
    // The ticks each driver channel writes a song-wide command or an $F4 $03 at under the options, by channel, which break its notes as its own commands do.
    public static IReadOnlyList<int>[] SongWideTicksByChannel(Song song, MmlExportOptions options)
    {
        return [.. SongWideCommandsByChannel(song, options).Select(commands => (IReadOnlyList<int>)[.. commands.Select(command => command.Tick).Distinct()])];
    }

    // The song-wide commands after the header, each in the channel that writes its tick, and the $F4 $03s where the options ask for them, by channel.
    private static List<TimedCommand>[] SongWideCommandsByChannel(Song song, MmlExportOptions options)
    {
        var commandsByChannel = Enumerable.Range(0, Song.DriverChannelCount).Select(_ => new List<TimedCommand>()).ToArray();
        var toggledChannelsByTick = options.SingleChannelEchoChangesAsF403 ? EchoToggledChannelsByTick(song) : new Dictionary<int, List<int>>();
        foreach (var (tick, channels) in toggledChannelsByTick)
        {
            foreach (int channel in channels)
            {
                commandsByChannel[channel].Add(new TimedCommand(tick, EchoSendOrder, EchoToggleName.Write()));
            }
        }

        var songWideChannelAt = SongWideChannelAt(song);
        foreach (var command in SongWideCommands(song, toggledChannelsByTick.Keys.ToHashSet()))
        {
            commandsByChannel[songWideChannelAt(command.Tick)].Add(command);
        }

        return commandsByChannel;
    }

    // The song-wide commands after the header: the tempo's, the song volume's, the echo's but for the $EFs at the ticks the channels toggle their bits at
    // instead, and the light staccato's toggles.
    private static IEnumerable<TimedCommand> SongWideCommands(Song song, IReadOnlySet<int> toggledSendTicks)
    {
        return LaneRegions.TempoCommands(song).SelectMany(command => RegionCommands('t', command))
            .Concat(LaneRegions.MasterVolumeCommands(song).SelectMany(command => RegionCommands('w', command)))
            .Concat(EchoCommands(song, toggledSendTicks))
            .Concat(StaccatoToggleTicks(song, LoopMarkerTick(song)).Select(tick => new TimedCommand(tick, StaccatoOrder, StaccatoName.Write())));
    }

    // The channel the song-wide commands of a tick are written in: the lowest-numbered written channel a note or a rest starts in there, and the command
    // channel at the loop marker's tick or where none does.
    private static Func<int, int> SongWideChannelAt(Song song)
    {
        int markerTick = LoopMarkerTick(song);
        var startTicksByChannel = Enumerable.Range(0, Song.DriverChannelCount)
            .Where(channel => WritesChannel(song, channel))
            .Select(channel => (Channel: channel, StartTicks: NoteOrRestStartTicks(song, channel)))
            .ToList();
        return tick => tick == markerTick
            ? CommandChannel
            : startTicksByChannel.Where(writtenChannel => writtenChannel.StartTicks.Contains(tick)).Select(writtenChannel => writtenChannel.Channel).DefaultIfEmpty(CommandChannel).First();
    }

    // The ticks a note or a rest starts at in a channel's stream: its first tick, and the start and the end of each of its notes.
    private static HashSet<int> NoteOrRestStartTicks(Song song, int channel)
    {
        return [0, .. song.NotesInsideSong(channel).SelectMany(note => new[] { note.StartTick, note.EndTick() })];
    }
}
