using System.IO;

namespace SpcStudio.Model;

// The song file's echo: the song's own FIR filters, the two echo volumes, the runs the echo plays over and each channel's send.
public static partial class SongFile
{
    // Writes the filters, the two volumes, the runs and every channel's send.
    private static void WriteEcho(Song song, SongDto contents)
    {
        contents.EchoFilters = [.. song.FilterTable.Skip(SnesEcho.PresetCount).Select(filter => SnesEcho.UnpackCoefficients(filter).Select(coefficient => (int)coefficient).ToArray())];
        contents.EchoSettings = [.. SnesEcho.VolumeSettings.Select(setting => new EchoSettingDto
        {
            Setting = setting,
            Value = song.BaseEchoVolume(setting),
            Changes = [.. song.EchoChanges(setting).Select(ChangeDto.Of)],
        })];
        contents.EchoRuns = [.. song.EchoRuns.Select(run => new EchoRunDto
        {
            Tick = run.StartTick,
            Ticks = run.LengthTicks,
            Delay = run.State.Delay,
            Feedback = run.State.Feedback,
            Filter = run.State.Filter,
        })];
        for (int channel = 0; channel < song.ChannelCount; channel++)
        {
            contents.Channels[channel].Echo = song.IsBaseChannelEchoOn(channel);
            contents.Channels[channel].EchoChanges = [.. song.ChannelEchoChanges(channel).Select(change => ChangeDto.Of(change.Tick, change.Value))];
        }
    }

    // Reads the song's echo back: the filters first, then the two volumes and the runs.
    private static void ReadEcho(Song song, SongDto contents, string fileName)
    {
        ReadEchoFilters(song, contents, fileName);
        ReadEchoVolumes(song, contents, fileName);
        ReadEchoRuns(song, contents, fileName);
    }

    // Reads the song's own filters back, refusing one that is not the full set of coefficients the DSP can read.
    private static void ReadEchoFilters(Song song, SongDto contents, string fileName)
    {
        foreach (int[] filter in contents.EchoFilters)
        {
            if (filter.Length != SnesEcho.TapCount)
            {
                throw new InvalidDataException($"{fileName} has an echo FIR filter of {filter.Length} coefficients, where the command carries {SnesEcho.TapCount}.");
            }

            foreach (int coefficient in filter)
            {
                SavedFileChecks.RequireInRange(fileName, "an echo FIR coefficient of", coefficient, new ValueRange(sbyte.MinValue, sbyte.MaxValue));
            }
        }

        song.SetFilterTable([.. SnesEcho.PackedPresets, .. contents.EchoFilters.Select(filter => SnesEcho.PackCoefficients([.. filter.Select(coefficient => (sbyte)coefficient)]))]);
    }

    // Reads the two echo volumes back as the one lane they are, refusing a file without exactly one entry for each.
    private static void ReadEchoVolumes(Song song, SongDto contents, string fileName)
    {
        var changesBySide = new Dictionary<EchoSetting, List<ChangeDto>>();
        foreach (var setting in SnesEcho.VolumeSettings)
        {
            string volumeNameInMessages = setting == EchoSetting.VolumeLeft ? "echo left volume" : "echo right volume";
            var entriesForVolume = contents.EchoSettings.Where(entry => entry.Setting == setting).ToList();
            if (entriesForVolume.Count != 1)
            {
                throw new InvalidDataException($"{fileName} does not have exactly one {volumeNameInMessages} setting.");
            }

            var range = song.EchoSettingRange(setting);
            SavedFileChecks.RequireInRange(fileName, $"an {volumeNameInMessages} of", entriesForVolume[0].Value, range);
            song.SetBaseEchoVolume(setting, entriesForVolume[0].Value);
            changesBySide[setting] = [];
            ReadChanges(entriesForVolume[0].Changes, fileName, volumeNameInMessages, range, canFade: true, changesBySide[setting].Add);
        }

        song.SetEchoVolumeChanges(
            changesBySide[EchoSetting.VolumeLeft].Select(change => change.AsLaneDivider()),
            changesBySide[EchoSetting.VolumeRight].Select(change => change.AsLaneDivider()));
    }

    // Reads the Echo Unit lane's runs back, refusing a setting out of the range the song takes.
    private static void ReadEchoRuns(Song song, SongDto contents, string fileName)
    {
        var runs = new List<EchoRun>();
        ReadUnits(
            contents.EchoRuns,
            run => run.Tick,
            run => run.Ticks,
            $"{fileName} echo run",
            (run, where) =>
            {
                SavedFileChecks.RequireInRange(where, "delay", run.Delay, SnesEcho.DelayRange);
                SavedFileChecks.RequireInRange(where, "feedback", run.Feedback, SnesEcho.FeedbackRange);
                SavedFileChecks.RequireInRange(where, "filter", run.Filter, song.FilterRange);
                runs.Add(new EchoRun(run.Tick, run.Ticks, new EchoUnitState(run.Delay, run.Feedback, run.Filter)));
            });
        song.SetEchoRuns(runs);
    }

    // Reads one channel's echo send back, its base bit and its changes.
    private static void ReadChannelEchoSend(Song song, int channel, ChannelDto channelContents, string channelName)
    {
        song.SetBaseChannelEchoOn(channel, channelContents.Echo);
        ReadChangesWithLaneName(
            channelContents.EchoChanges,
            $"{channelName} echo change",
            "echo send",
            new ValueRange(Song.ChannelEchoOff, Song.ChannelEchoOn),
            canFade: false,
            change => song.SetChannelEchoChange(channel, change.Tick, change.Value));
    }

    // One of the two echo volumes: which one, its base value and the changes over it.
    private sealed class EchoSettingDto
    {
        public EchoSetting Setting { get; set; }
        public int Value { get; set; }
        public List<ChangeDto> Changes { get; set; } = [];
    }

    // One stretch of the song the echo runs over, and the three settings it runs with.
    private sealed class EchoRunDto
    {
        public int Tick { get; set; }
        public int Ticks { get; set; }
        public int Delay { get; set; }
        public int Feedback { get; set; }
        public int Filter { get; set; }
    }
}
