using System.IO;

namespace SpcStudio.Model;

// The song file's lanes of changes over a base: the tempo, the song volume, and each channel's volume, pan and amplify, with the one reader every
// lane of changes is read back through, the echo's included.
public static partial class SongFile
{
    // Writes the tempo, the song volume and every channel's volume, pan and amplify.
    private static void WriteChangeLanes(Song song, SongDto contents)
    {
        contents.TempoChanges = [.. song.TempoChanges.Select(ChangeDto.Of)];
        contents.VolumeChanges = [.. song.VolumeChanges.Select(ChangeDto.Of)];
        for (int channel = 0; channel < song.ChannelCount; channel++)
        {
            var channelContents = contents.Channels[channel];
            channelContents.Volume = song.BaseChannelVolume(channel);
            channelContents.VolumeChanges =
                [.. song.ChannelVolumeChanges(channel).Select(ChangeDto.Of)];
            channelContents.Pan = song.BasePan(channel);
            channelContents.SurroundLeft = song.BaseSurround(channel).IsLeftInverted;
            channelContents.SurroundRight = song.BaseSurround(channel).IsRightInverted;
            channelContents.PanChanges =
                [.. song.PanChanges(channel).Select(change => ChangeDto.Of(change.Tick, change.Value, change.Fades, change.FadeTo, change.Surround))];
            channelContents.Amplify = song.BaseAmplify(channel);
            channelContents.AmplifyChanges = [.. song.AmplifyChanges(channel).Select(change => ChangeDto.Of(change.Tick, change.Value))];
        }
    }

    // Reads the tempo's and the song volume's changes back.
    private static void ReadSongChangeLanes(Song song, SongDto contents, string fileName)
    {
        ReadChanges(
            contents.TempoChanges,
            fileName,
            SavedFileChecks.TempoName,
            SnesTiming.TempoRange,
            canFade: true,
            change => song.SetTempoChange(change.Tick, change.Value, change.Fades, change.FadeTo));
        ReadChanges(
            contents.VolumeChanges,
            fileName,
            SavedFileChecks.SongVolumeName,
            SnesVolume.MasterVolumeRange,
            canFade: true,
            change => song.SetVolumeChange(change.Tick, change.Value, change.Fades, change.FadeTo));
    }

    // Reads one channel's volume, pan and amplify back, its bases and its changes, refusing a base out of range.
    private static void ReadChannelChangeLanes(Song song, int channel, ChannelDto channelContents, string channelName)
    {
        SavedFileChecks.RequireVolume(channelName, channelContents.Volume);
        song.SetBaseChannelVolume(channel, channelContents.Volume);
        ReadChanges(
            channelContents.VolumeChanges,
            channelName,
            SavedFileChecks.VolumeName,
            SnesVolume.ChannelVolumeRange,
            canFade: true,
            change => song.SetChannelVolumeChange(channel, change.Tick, change.Value, change.Fades, change.FadeTo));

        SavedFileChecks.RequirePan(channelName, channelContents.Pan);
        song.SetBasePan(channel, channelContents.Pan);
        song.SetBaseSurround(channel, new SnesSurround(channelContents.SurroundLeft, channelContents.SurroundRight));
        ReadChanges(
            channelContents.PanChanges,
            channelName,
            SavedFileChecks.PanName,
            SnesVolume.PanIndexRange,
            canFade: true,
            change => song.SetPanChange(
                channel, change.Tick, change.Value, change.Fades, change.FadeTo, new SnesSurround(change.SurroundLeft, change.SurroundRight)));

        SavedFileChecks.RequireAmplify(channelName, channelContents.Amplify);
        song.SetBaseAmplify(channel, channelContents.Amplify);
        ReadChanges(
            channelContents.AmplifyChanges,
            channelName,
            SavedFileChecks.AmplifyName,
            SnesVolume.AmplifyRange,
            canFade: false,
            change => song.SetAmplifyChange(channel, change.Tick, change.Value));
    }

    // Reads a lane's changes back, the lane named in its messages after what owns it and its base.
    private static void ReadChanges(IEnumerable<ChangeDto> changes, string ownerName, string baseName, ValueRange range, bool canFade, Action<ChangeDto> place)
    {
        ReadChangesWithLaneName(changes, $"{ownerName} {baseName} change", baseName, range, canFade, place);
    }

    // Reads a lane's changes back in tick order and hands each one on, refusing a change on a tick its base owns, a value or a fade target out of
    // range, a fade on a lane that cannot fade and a second change on one tick.
    private static void ReadChangesWithLaneName(
        IEnumerable<ChangeDto> changes,
        string laneName,
        string baseName,
        ValueRange range,
        bool canFade,
        Action<ChangeDto> place)
    {
        int previousTick = Song.TickBeforeSong;
        foreach (var change in changes.OrderBy(change => change.Tick))
        {
            string where = $"{laneName} at tick {change.Tick}";
            if (change.Fades && !canFade)
            {
                throw new InvalidDataException($"{where} fades, which a {baseName} change cannot.");
            }

            if (!Song.IsFadeChangeTick(change.Tick, change.Fades))
            {
                throw new InvalidDataException($"{where} is at or before tick 0, which the base {baseName} owns.");
            }

            SavedFileChecks.RequireInRange(where, "value", change.Value, range);
            if (change.Fades)
            {
                SavedFileChecks.RequireInRange(where, "fade target", change.FadeTo, range);
            }
            else if (change.FadeTo != 0)
            {
                throw new InvalidDataException($"{where} has a fade target of {change.FadeTo} but does not fade.");
            }

            if (change.Tick == previousTick)
            {
                throw new InvalidDataException($"{where} is the second one at that tick.");
            }

            place(change);
            previousTick = change.Tick;
        }
    }

    // One change in a lane: from its tick on, the song or the channel plays at its value, ramping to the fade target where it fades.
    private sealed class ChangeDto
    {
        public int Tick { get; set; }
        public int Value { get; set; }
        public bool Fades { get; set; }
        public int FadeTo { get; set; }
        public bool SurroundLeft { get; set; }
        public bool SurroundRight { get; set; }

        // The divider this change read back is.
        public LaneDivider<int> AsLaneDivider()
        {
            return new(Tick, Value, Fades, FadeTo);
        }

        // A lane divider as it is written.
        public static ChangeDto Of(LaneDivider<int> divider)
        {
            return Of(divider.Tick, divider.Value, divider.Fades, divider.FadeTo);
        }

        // A change as it is written.
        public static ChangeDto Of(int tick, int value, bool fades = false, int fadeTo = 0, SnesSurround surround = default)
        {
            return new ChangeDto
            {
                Tick = tick,
                Value = value,
                Fades = fades,
                FadeTo = fadeTo,
                SurroundLeft = surround.IsLeftInverted,
                SurroundRight = surround.IsRightInverted,
            };
        }
    }
}
