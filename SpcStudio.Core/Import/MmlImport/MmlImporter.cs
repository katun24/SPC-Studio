using System.IO;
using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

// Turns a parsed MML file into a song, refusing the whole file wherever the song could not play as the ROM plays it.
public static partial class MmlImporter
{
    public const string DialogFilter = "AddmusicK MML (*.txt)|*.txt" + DialogFilters.Separator + DialogFilters.AllFiles;
    public const string Extension = ".txt";
    public const int StageCount = 3;
    private const int EventsPerProgressReport = 1024;
    private const string ReadingStage = "Reading the song";
    private const string EventsReadStage = "events read";

    // Reads a song's text, the samples it names and any sample group it pulls in, from the AddmusicK install the song sits in.
    public static Song Load(string path, Action<string>? warn = null, Action<string, int, int>? progress = null, CancellationToken cancel = default)
    {
        progress?.Invoke(ReadingStage, 0, 0);
        var file = MmlParser.Parse(File.ReadAllText(path));
        string songFolder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        return BuildFromInstall(file, AmkRoot(songFolder), songFolder, warn, progress, cancel);
    }

    // Binds a parsed file to a song, reading each sample it names from beside the song or from that AddmusicK install, and any sample group it
    // pulls in from the install, refusing a group the install does not define.
    public static Song BuildFromInstall(
        MmlFile file, string amkRoot, string songFolder, Action<string>? warn = null, Action<string, int, int>? progress = null,
        CancellationToken cancel = default)
    {
        var sampleGroupsByName = new Lazy<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(() => SampleGroups.ReadPathsByGroup(amkRoot));
        if (file.Samples.FirstOrDefault(entry => entry.IsGroup && sampleGroupsByName.Value?.ContainsKey(entry.Name) != true) is { } undefinedGroup)
        {
            throw MmlImportException.SampleGroupNotInInstall(undefinedGroup.Name, undefinedGroup.Line);
        }

        return Build(
            file,
            name =>
            {
                string samplePath = AmkSampleLookup.PathOf(amkRoot, songFolder, name);
                if (!File.Exists(samplePath))
                {
                    throw MmlImportException.SampleNotFound(name, AmkSampleLookup.BesideSong(songFolder, name), samplePath);
                }

                return File.ReadAllBytes(samplePath);
            },
            group => sampleGroupsByName.Value is { } defined && defined.TryGetValue(group, out var files) ? files : null,
            warn, progress, cancel);
    }

    // The AddmusicK install a song sits in: the nearest folder at or above the song's own that holds AddmusicK or its sample groups file, or the song folder's parent.
    private static string AmkRoot(string songFolder)
    {
        for (var folder = new DirectoryInfo(songFolder); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, AmkRun.ExeName)) || File.Exists(Path.Combine(folder.FullName, SampleGroups.FileName)))
            {
                return folder.FullName;
            }
        }

        return Directory.GetParent(songFolder)?.FullName ?? songFolder;
    }

    // Binds a parsed file to a song, taking sample bytes and sample groups from the two readers rather than from a disk.
    public static Song Build(
        MmlFile file, Func<string, byte[]?> readSample, Func<string, IReadOnlyList<string>?>? readGroup = null, Action<string>? warn = null,
        Action<string, int, int>? progress = null, CancellationToken cancel = default)
    {
        var song = NewSong(file);
        file = RewriteStreamsBeforeWalk(file, warn);
        var bound = BindLibrary(song, file, readSample, readGroup, progress, cancel);
        var globalTransposes = GlobalTransposes.From(file.Channels);

        // The driver's wait time at any tick, read off the light staccato toggles the first time it is asked for.
        var waitTimeAt = new Lazy<Func<int, int>>(() => MmlRemoteExpansion.WaitTimeLookup(file));

        var walked = WalkChannels(song, file, bound, globalTransposes, waitTimeAt, progress, cancel);
        int songEndTick = walked.EndTick ?? 0;
        if (songEndTick < 1)
        {
            throw MmlImportException.SongZeroTicks();
        }

        WarnTransposesAcrossLoop(file.DoesLoop, walked.TransposesAcrossLoop, songEndTick, warn);
        WarnGlobalTransposeAcrossLoop(file, walked.LoopTick, globalTransposes, songEndTick, warn);
        WarnDisagreeingLoopPoints(walked.LoopTicksByChannel, file.Channels.Count, warn);
        WarnNotesAtNoPitch(walked, warn);
        WarnIgnoredCommands(walked.IgnoredCommands, warn);
        var regionCommands = new RegionLaneCommands();
        var secondVibratoFades = new List<(int Channel, int Tick)>();
        ReadSongWideRegionLanes(song, walked, regionCommands, warn);
        for (int channel = 0; channel < Song.DriverChannelCount; channel++)
        {
            ApplyChannelLanes(song, file, channel, walked, regionCommands, secondVibratoFades, warn);
        }

        WarnSecondVibratoFades(secondVibratoFades, warn);
        ApplyEcho(song, walked, file.DoesLoop ? walked.LoopTick ?? 0 : null, warn);
        ApplyStaccato(song, walked.StaccatoToggles, songEndTick);
        if (walked.HasTempoImmunity)
        {
            song.HasTempoImmunity = true;
        }

        ApplyVelocityTable(song, walked.VelocityTableSwitches);
        PlaceMarkers(song, walked.LoopTick, songEndTick);
        ApplyRegionLanes(song, regionCommands, warn);
        var cutsHeard = new CutsHeard();
        for (int channel = 0; channel < Song.DriverChannelCount; channel++)
        {
            CutNotes(song, channel, walked.CutsInsideNotes[channel], TuningCuts(song, channel, walked.EnvelopeWrites[channel]), walked.NotesReadFrom, waitTimeAt, cutsHeard);
            ApplyEnvelope(song, channel, walked.EnvelopeWrites[channel]);
            VerifyEnvelope(song, channel, walked.EnvelopeWrites[channel]);
        }

        WarnCutsHeard(cutsHeard, warn);
        RemoveSilencedInstruments(song, bound);
        SharedNoteValues.MoveMajorityValuesOntoInstruments(song);
        WarnManyNotes(song, warn);
        return song;
    }

    // An empty song carrying the file's own settings.
    private static Song NewSong(MmlFile file)
    {
        return new Song
        {
            IsLooping = file.DoesLoop,
            HasTempoImmunity = file.HasTempoImmunity,
            SpcTags = file.SpcTags,
            SamplePath = DiskNameRules.Normalize(file.SamplePath),
            SampleGroup = SampleGroupOf(file),
        };
    }

    // The file with every construct the walk does not read rewritten into ordinary events, in the order each depends on the one before.
    private static MmlFile RewriteStreamsBeforeWalk(MmlFile file, Action<string>? warn)
    {
        file = MmlRestBends.LiftOffStreams(file);
        MmlArpeggioExpansion.RefuseInRemoteCode(file);
        MmlArpeggioExpansion.RefuseBesideRemoteCode(file);
        RefuseHeldCutTies(file);
        file = file with { Channels = MmlRemoteExpansion.ExpandChannels(file, warn) };
        file = file with { Channels = SoundThroughUnmadeCuts(file) };
        file = file with { Channels = SoundThroughLegato(file) };
        file = file with { Channels = MmlRestBends.PlaceOnNotes(file.Channels) };
        file = file with { Channels = ResolveFineTune(file.Channels) };
        return file with { Channels = MmlArpeggioExpansion.ExpandChannels(file, warn) };
    }

    // Walks every channel in turn, placing the notes and gathering everything else, and reports how many events have been read.
    private static WalkedCommands WalkChannels(
        Song song, MmlFile file, BoundLibrary bound, GlobalTransposes globalTransposes, Lazy<Func<int, int>> waitTimeAt, Action<string, int, int>? progress,
        CancellationToken cancel)
    {
        var walked = new WalkedCommands();
        int eventCount = file.Channels.Sum(channel => channel.Events.Count);
        int eventsRead = 0;
        progress?.Invoke(EventsReadStage, 0, eventCount);

        // Counts one event read, reporting the count and checking for cancellation once in a while.
        void CountEventRead()
        {
            if (++eventsRead % EventsPerProgressReport != 0)
            {
                return;
            }

            cancel.ThrowIfCancellationRequested();
            progress?.Invoke(EventsReadStage, eventsRead, eventCount);
        }

        foreach (var channel in file.Channels)
        {
            new ChannelWalk(song, file, channel, bound, globalTransposes, waitTimeAt, walked, CountEventRead).Run();
        }

        return walked;
    }

    // Reads the tempo and the song volume commands into the base values and the commands their regions are made of.
    private static void ReadSongWideRegionLanes(Song song, WalkedCommands walked, RegionLaneCommands regionCommands, Action<string>? warn)
    {
        ApplyFadedLane(
            SongWideInDriverOrder(walked.Tempos), walked.LoopTick, "tempo", song.BaseTempo, (value, _) => song.BaseTempo = value, regionCommands.Tempos, warn);
        ApplyFadedLane(
            SongWideInDriverOrder(walked.MasterVolumes), walked.LoopTick, "song volume", song.BaseMasterVolume, (value, _) => song.BaseMasterVolume = value,
            regionCommands.MasterVolumes, warn);
    }

    // Reads one channel's own lanes: its volume and pan into region commands, and its amplify, vibrato, tremolo and legato onto the song.
    private static void ApplyChannelLanes(
        Song song, MmlFile file, int channel, WalkedCommands walked, RegionLaneCommands regionCommands, List<(int Channel, int Tick)> secondVibratoFades, Action<string>? warn)
    {
        ApplyFadedLane(
            walked.ChannelVolumes[channel], walked.LoopTick, $"channel #{channel} volume", song.BaseChannelVolume(channel),
            (value, _) => song.SetBaseChannelVolume(channel, value), regionCommands.ChannelVolumes[channel], warn);
        ApplyFadedLane(
            walked.Pans[channel], walked.LoopTick, $"channel #{channel} pan", song.BasePan(channel),
            (value, surround) =>
            {
                song.SetBasePan(channel, value);
                song.SetBaseSurround(channel, surround);
            },
            regionCommands.Pans[channel], warn);
        ApplyLane(
            [.. walked.Amplifies[channel].Select(command => (command.Tick, command.Value, command.Line))], song.BaseAmplify(channel),
            value => song.SetBaseAmplify(channel, value), (tick, value) => song.SetAmplifyChange(channel, tick, value));
        ApplyVibrato(song, channel, walked.Vibratos[channel], secondVibratoFades);
        ApplyTremolo(song, channel, walked.Tremolos[channel]);
        if (file.Channels.FirstOrDefault(written => written.Number == channel) is { } writtenChannel)
        {
            ApplyLegato(song, writtenChannel, walked.SilentNoteTicks[channel]);
        }
    }

    // Puts the end and loop markers where the file says, refusing a loop point at or past the end.
    private static void PlaceMarkers(Song song, int? loopTick, int songEndTick)
    {
        if (loopTick is { } loop && loop >= songEndTick)
        {
            throw MmlImportException.LoopPointNotBeforeEnd(loop, songEndTick);
        }

        song.SetMarkers(songEndTick, loopTick ?? 0);
    }

    // Turns the collected commands of the four region lanes into their regions, now that the roll's length is known.
    private static void ApplyRegionLanes(Song song, RegionLaneCommands regionCommands, Action<string>? warn)
    {
        ApplyRegions(regionCommands.Tempos, "tempo", (tick, value, fades, fadeTo, _) => song.SetTempoChange(tick, value, fades, fadeTo), warn);
        ApplyRegions(
            regionCommands.MasterVolumes, "song volume", (tick, value, fades, fadeTo, _) => song.SetVolumeChange(tick, value, fades, fadeTo), warn);
        for (int channel = 0; channel < Song.DriverChannelCount; channel++)
        {
            int number = channel;
            ApplyRegions(
                regionCommands.ChannelVolumes[channel], $"channel #{number} volume",
                (tick, value, fades, fadeTo, _) => song.SetChannelVolumeChange(number, tick, value, fades, fadeTo), warn);
            ApplyRegions(
                regionCommands.Pans[channel], $"channel #{number} pan",
                (tick, value, fades, fadeTo, surround) => song.SetPanChange(number, tick, value, fades, fadeTo, surround), warn);
        }
    }

    // Takes out the instruments whose envelope can never open, with every note that plays one and any sample made for one that nothing else plays.
    private static void RemoveSilencedInstruments(Song song, BoundLibrary bound)
    {
        if (bound.SilencedInstruments.Count == 0)
        {
            return;
        }

        foreach (int instrumentId in bound.SilencedInstruments)
        {
            song.DeleteInstrument(instrumentId);
        }

        foreach (int sample in bound.SynthesizedSampleIds)
        {
            if (song.InstrumentCountUsingSample(sample) == 0)
            {
                song.DeleteSample(sample);
            }
        }
    }

    // One empty list for each channel.
    private static List<T>[] ListPerChannel<T>()
    {
        return [.. Enumerable.Range(0, Song.DriverChannelCount).Select(_ => new List<T>())];
    }

    // The commands of the four region lanes as the file wrote them, held until the roll's length is known.
    private sealed class RegionLaneCommands
    {
        public List<CollectedRegionCommand> Tempos { get; } = [];
        public List<CollectedRegionCommand> MasterVolumes { get; } = [];
        public List<CollectedRegionCommand>[] ChannelVolumes { get; } = ListPerChannel<CollectedRegionCommand>();
        public List<CollectedRegionCommand>[] Pans { get; } = ListPerChannel<CollectedRegionCommand>();
    }
}
