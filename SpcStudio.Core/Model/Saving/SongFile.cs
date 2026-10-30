using System.IO;

namespace SpcStudio.Model;

// The DAW's own song format, which holds everything needed to go on editing a song. This part is the format, the song's own settings and its
// markers; the other five are its samples and instruments, its change lanes, its echo, its unit lanes and its notes.
public static partial class SongFile
{
    public const string Extension = ".spcproj";
    public const string DialogFilter = "SPC Studio song (*.spcproj)|*.spcproj" + DialogFilters.Separator + DialogFilters.AllFiles;
    private const string FileKind = "song";
    private const int CurrentVersion = 61;

    // Writes a song.
    public static void Save(string path, Song song)
    {
        // The file's contents, with an empty entry for each channel.
        var contents = new SongDto
        {
            Version = CurrentVersion,
            Channels = [.. Enumerable.Range(0, song.ChannelCount).Select(_ => new ChannelDto())],
        };
        WriteSettings(song, contents);
        WriteLibraries(song, contents);
        WriteChangeLanes(song, contents);
        WriteEcho(song, contents);
        WriteUnitLanes(song, contents);
        WriteNotes(song, contents);
        SavedFileJson.WriteLeavingOutDefaults(path, contents);
    }

    // Reads a song back, refusing a file that is not one of ours or holds anything the song could not as one that is not a valid song file.
    public static Song Load(string path)
    {
        return SavedFileJson.ReadRefusingAsNotValid(path, FileKind, ReadSong);
    }

    // Reads a song back, refusing a file that is not one of ours or holds anything the song could not, each refusal saying what is wrong with it.
    private static Song ReadSong(string path)
    {
        string quotedFileName = SavedFileJson.QuotedFileName(path);

        // The file's contents, refused where it is not a song file of the current version.
        var contents = SavedFileJson.Read<SongDto>(path, FileKind, CurrentVersion);

        // The song made with the file's own settings, which the rest of the file is read into.
        var song = ReadSettings(contents, quotedFileName);
        ReadSongChangeLanes(song, contents, quotedFileName);
        ReadEcho(song, contents, quotedFileName);
        ReadLibraries(song, contents, quotedFileName);
        song.SetChannelCount(contents.Channels.Count);
        for (int channel = 0; channel < song.ChannelCount; channel++)
        {
            var channelContents = contents.Channels[channel];
            string channelName = $"{quotedFileName} channel {channel + 1}";
            song.Muting.SetMuted(channel, channelContents.Muted);
            song.Muting.SetSoloed(channel, channelContents.Soloed);
            ReadChannelChangeLanes(song, channel, channelContents, channelName);
            ReadChannelEchoSend(song, channel, channelContents, channelName);
            ReadNotes(song, channel, channelContents, channelName);
        }

        ReadStaccatoUnits(song, contents, quotedFileName);
        ReadMarkers(song, contents, quotedFileName);
        return song;
    }

    // Writes the song's own settings, its markers and each channel's mute and solo.
    private static void WriteSettings(Song song, SongDto contents)
    {
        contents.Tempo = song.BaseTempo;
        contents.SnapTicks = song.SnapTicks;
        contents.BeatsPerBar = song.TimeSignature.BeatsPerBar;
        contents.BeatUnit = song.TimeSignature.BeatUnit;
        contents.Zoom = song.HorizontalZoom;
        contents.VerticalZoom = song.VerticalZoom;
        contents.LaneHeights = new Dictionary<string, double>(song.LaneHeights);
        contents.LaneOrder = [.. song.LaneOrder];
        contents.PanelLayout = song.PanelLayout.Copy();
        contents.EndTick = song.EndTick;
        contents.LoopTick = song.LoopTick;
        contents.Loops = song.IsLooping;
        contents.TempoImmunity = song.HasTempoImmunity;
        contents.SmwVelocities = song.UsesSmwVelocities;
        contents.EndEnabled = song.IsEndEnabled;
        contents.SamplePath = song.SamplePath;
        contents.SampleGroup = song.SampleGroup;
        contents.SpcTitle = song.SpcTags.Title;
        contents.SpcAuthor = song.SpcTags.Author;
        contents.SpcGame = song.SpcTags.Game;
        contents.SpcComment = song.SpcTags.Comment;
        contents.MasterVolume = song.BaseMasterVolume;
        for (int channel = 0; channel < song.ChannelCount; channel++)
        {
            contents.Channels[channel].Muted = song.Muting.IsMuted(channel);
            contents.Channels[channel].Soloed = song.Muting.IsSoloed(channel);
        }
    }

    // A song made with the file's own settings, each refused where the song could not hold it and each view setting left at its default where it makes no sense.
    private static Song ReadSettings(SongDto contents, string fileName)
    {
        SavedFileChecks.RequireInRange(fileName, "channel count", contents.Channels.Count, new ValueRange(Song.DriverChannelCount, Song.MaxChannelCount));
        SavedFileChecks.RequireInRange(fileName, SavedFileChecks.TempoName, contents.Tempo, SnesTiming.TempoRange);
        SavedFileChecks.RequireInRange(fileName, SavedFileChecks.SongVolumeName, contents.MasterVolume, SnesVolume.MasterVolumeRange);
        if (!SnapSize.IsValid(contents.SnapTicks))
        {
            throw new InvalidDataException($"{fileName} snaps to {contents.SnapTicks} ticks, which is not one of the snap sizes.");
        }

        var timeSignature = new TimeSignature(contents.BeatsPerBar, contents.BeatUnit);
        return new Song
        {
            BaseTempo = contents.Tempo,
            BaseMasterVolume = contents.MasterVolume,
            SnapTicks = contents.SnapTicks,
            TimeSignature = timeSignature.IsValid ? timeSignature : TimeSignature.Default,
            HorizontalZoom = contents.Zoom is double zoom && IsViewScale(zoom) ? zoom : null,
            VerticalZoom = IsViewScale(contents.VerticalZoom) ? contents.VerticalZoom : 1,
            LaneHeights = (contents.LaneHeights ?? []).Where(entry => IsViewScale(entry.Value)).ToDictionary(entry => entry.Key, entry => entry.Value),
            LaneOrder = [.. (contents.LaneOrder ?? []).Distinct()],
            PanelLayout = contents.PanelLayout ?? new PanelLayout(),
            IsLooping = contents.Loops,
            HasTempoImmunity = contents.TempoImmunity,
            UsesSmwVelocities = contents.SmwVelocities,
            SamplePath = DiskNameRules.Normalize(contents.SamplePath),
            SampleGroup = string.IsNullOrWhiteSpace(contents.SampleGroup) ? null : contents.SampleGroup,
            IsEndEnabled = contents.EndEnabled,
            SpcTags = new SpcTags(contents.SpcTitle ?? "", contents.SpcAuthor ?? "", contents.SpcGame ?? "", contents.SpcComment ?? ""),
        };
    }

    // Whether a zoom or a lane height is a size the view can draw at.
    private static bool IsViewScale(double scale)
    {
        return double.IsFinite(scale) && scale > 0;
    }

    // Puts the two markers down, refusing either off the roll the end marker opens and a loop that does not start before the end.
    private static void ReadMarkers(Song song, SongDto contents, string fileName)
    {
        song.SetMarkers(contents.EndTick, contents.LoopTick);
        if (!song.IsMarkerTick(contents.EndTick) || !song.IsMarkerTick(contents.LoopTick))
        {
            throw new InvalidDataException(
                $"{fileName} ends at tick {contents.EndTick} and loops from tick {contents.LoopTick}, and both have to be within 0..{song.RollLengthTicks}.");
        }

        if (contents.LoopTick >= contents.EndTick)
        {
            throw new InvalidDataException($"{fileName} loops from tick {contents.LoopTick}, which is not before its end at {contents.EndTick}.");
        }
    }

    // The file's own layout, top to bottom.
    private sealed class SongDto : IVersionedContents
    {
        public int Version { get; set; }
        public int Tempo { get; set; }
        public int SnapTicks { get; set; }
        public int BeatsPerBar { get; set; } = TimeSignature.Default.BeatsPerBar;
        public int BeatUnit { get; set; } = TimeSignature.Default.BeatUnit;
        public double? Zoom { get; set; }
        public double VerticalZoom { get; set; } = 1;
        public Dictionary<string, double>? LaneHeights { get; set; }
        public List<string>? LaneOrder { get; set; }
        public PanelLayout? PanelLayout { get; set; }
        public List<ChangeDto> TempoChanges { get; set; } = [];
        public int MasterVolume { get; set; }
        public List<ChangeDto> VolumeChanges { get; set; } = [];
        public List<StaccatoUnitDto> StaccatoUnits { get; set; } = [];
        public int EndTick { get; set; }
        public int LoopTick { get; set; }
        public bool Loops { get; set; }
        public bool TempoImmunity { get; set; }
        public bool SmwVelocities { get; set; }
        public bool EndEnabled { get; set; }
        public string? SamplePath { get; set; }
        public string? SampleGroup { get; set; }
        public string? SpcTitle { get; set; }
        public string? SpcAuthor { get; set; }
        public string? SpcGame { get; set; }
        public string? SpcComment { get; set; }
        public List<EchoSettingDto> EchoSettings { get; set; } = [];
        public List<EchoRunDto> EchoRuns { get; set; } = [];
        public List<int[]> EchoFilters { get; set; } = [];
        public List<SongSampleDto> Samples { get; set; } = [];
        public List<SongInstrumentDto> Instruments { get; set; } = [];
        public List<ChannelDto> Channels { get; set; } = [];
    }

    // One channel's part of the file: its notes, its mute and solo, and its own lanes.
    private sealed class ChannelDto
    {
        public NoteDto[] Notes { get; set; } = [];
        public bool Muted { get; set; }
        public bool Soloed { get; set; }
        public int Volume { get; set; }
        public List<ChangeDto> VolumeChanges { get; set; } = [];
        public int Pan { get; set; }
        public bool SurroundLeft { get; set; }
        public bool SurroundRight { get; set; }
        public List<ChangeDto> PanChanges { get; set; } = [];
        public int Amplify { get; set; }
        public List<ChangeDto> AmplifyChanges { get; set; } = [];
        public bool Echo { get; set; }
        public List<ChangeDto> EchoChanges { get; set; } = [];
    }
}
