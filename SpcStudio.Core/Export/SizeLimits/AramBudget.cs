using System.Buffers.Binary;
using System.IO;
using SpcStudio.Audio;
using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio.Export;

// What a song costs the SPC700's ARAM and how much of it the install leaves to spend, laid out the way AddmusicK places a song.
public static class AramBudget
{
    public const string DriverPath = "asm/SNES/bin/main.bin";
    private const int PageBytes = 0x100;
    private const int DriverHeaderBytes = 4;
    private const int ProgramAddressOffset = 2;

    // How much of ARAM the install leaves one local song, read off its compiled driver's header, or null where that cannot be read.
    public static int? AvailableBytes(string? amkRoot)
    {
        return ReadProgramSize(amkRoot)?.AvailableBytes;
    }

    // Reads the budget, what the program under the song is made of and what every sample group loads out of the install, or null where the budget
    // or the program's parts cannot be read.
    public static AramInstall? ReadInstall(string? amkRoot)
    {
        if (ReadProgramSize(amkRoot) is not (int programBytes, int availableBytes) || AmkProgramParts.Read(amkRoot!, programBytes) is not { } programParts)
        {
            return null;
        }

        var groups = SampleGroups.ReadGroups(amkRoot!) ?? [];
        string samplesFolder = Path.Combine(amkRoot!, AmkInstallFolders.Samples);
        var loadByGroup = new Dictionary<string, SampleGroupLoad>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            loadByGroup[group.Name] = SampleGroupLoadOf(group, samplesFolder);
        }

        return new AramInstall(availableBytes, programParts, groups, loadByGroup);
    }

    // What the song costs in each part of ARAM against the install, given its compiled data's size.
    public static AramUsage Usage(Song song, AramInstall install, int songDataBytes)
    {
        int songStart = install.SongStartAddress;
        int roundedSongDataBytes = RoundUpToPage(songStart + songDataBytes) - songStart;

        var usedInstruments = MmlExporter.UsedInstruments(song);
        var ownSamples = SampleExport.UsedSamples(song, usedInstruments);
        var playedSampleIds = usedInstruments.Select(instrument => instrument.SampleId).ToHashSet();
        var placedBlocks = new List<byte[]>();

        // The sample group the song loads, AddmusicK's default where it names neither a group nor a sample of its own.
        string? loadedGroup = song.SampleGroup ?? (ownSamples.Count == 0 ? SampleGroups.DefaultGroup : null);

        var groupLoad = loadedGroup is not null ? install.LoadBySampleGroup.GetValueOrDefault(loadedGroup) : null;
        int groupBytes = groupLoad is not null ? SampleGroupBytes(groupLoad, playedSampleIds, placedBlocks) : 0;
        int sampleBytes = OwnSampleBytes(ownSamples, placedBlocks);
        int sampleTableBytes = (groupLoad?.TableBytes ?? 0) + ownSamples.Count * Sample.SampleTableBytes;

        int echoBytes = Math.Max(EchoBufferBytes(song), install.GlobalSongsEchoBufferBytes);
        int freeBytes = install.FreeBytesAfter(roundedSongDataBytes + sampleTableBytes + groupBytes + sampleBytes + echoBytes);

        return new AramUsage(roundedSongDataBytes, sampleTableBytes, groupBytes, ownSamples.Count, sampleBytes, echoBytes, freeBytes);
    }

    // What the song's echo buffer costs, which is sized by the largest delay the song ever asks for.
    public static int EchoBufferBytes(Song song)
    {
        int peakDelay = SnesEcho.MinDelay;

        foreach (var run in song.EchoRuns)
        {
            peakDelay = Math.Max(peakDelay, run.State.Delay);
        }

        return peakDelay * SnesEcho.BytesPerDelayStep;
    }

    // The largest echo buffer any of the install's global songs asks for, a song that cannot be read asking for none.
    public static int GlobalSongsEchoBufferBytes(string amkRoot)
    {
        AmkSongList songList;
        try
        {
            songList = AmkSongList.Read(amkRoot);
        }
        catch (AmkSongListException)
        {
            return 0;
        }

        return songList.GlobalSongs.Values.Select(listName => ImportedEchoBufferBytes(AmkSongList.SongPath(amkRoot, listName), amkRoot)).DefaultIfEmpty(0).Max();
    }

    // What the echo buffer of the song an MML file in an install holds costs, or nothing where the file cannot be imported.
    private static int ImportedEchoBufferBytes(string mmlPath, string amkRoot)
    {
        try
        {
            return EchoBufferBytes(MmlImporter.Load(mmlPath, amkRoot));
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // The size of the install's compiled program and the room it leaves one local song, read off its header, or null where that cannot be read.
    private static (int ProgramBytes, int AvailableBytes)? ReadProgramSize(string? amkRoot)
    {
        if (string.IsNullOrEmpty(amkRoot) || ReadDriverHeader(Path.Combine(amkRoot, DriverPath)) is not { } header)
        {
            return null;
        }

        int programBytes = BinaryPrimitives.ReadUInt16LittleEndian(header);
        int programAddress = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(ProgramAddressOffset));
        int availableBytes = SpcReader.AramSize - programBytes - programAddress;

        return availableBytes > 0 ? (programBytes, availableBytes) : null;
    }

    // The first bytes of the compiled driver, or null where the file cannot be read or is shorter than its header.
    private static byte[]? ReadDriverHeader(string driverPath)
    {
        return ReadableFiles.BytesOrNull(driverPath) is { Length: >= DriverHeaderBytes } driverBytes ? driverBytes[..DriverHeaderBytes] : null;
    }

    // What a sample group can load: a table entry for each entry, and each entry's blocks with whether it is marked important.
    private static SampleGroupLoad SampleGroupLoadOf(SampleGroup group, string samplesFolder)
    {
        return new SampleGroupLoad(group.Entries.Count * Sample.SampleTableBytes, [.. group.Entries.Select(entry => (BlocksOrNull(samplesFolder, entry.Path), entry.IsImportant))]);
    }

    // A group entry's blocks, or null where the disk does not hold its file as a sample.
    private static byte[]? BlocksOrNull(string samplesFolder, string entryPath)
    {
        if (ReadableFiles.BytesOrNull(Path.Combine(samplesFolder, entryPath)) is not { } fileBytes)
        {
            return null;
        }

        try
        {
            return BrrSample.BlockBytes(fileBytes, entryPath);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    // What the loaded group's samples cost: the blocks of each sample it loads - one marked important or one the song plays - where no earlier one holds
    // the same blocks.
    private static int SampleGroupBytes(SampleGroupLoad groupLoad, IReadOnlySet<int> playedSampleIds, List<byte[]> placedBlocks)
    {
        int bytes = 0;
        for (int position = 0; position < groupLoad.SamplesByPosition.Count; position++)
        {
            var (blocks, isImportant) = groupLoad.SamplesByPosition[position];
            if (blocks is null || (!isImportant && !playedSampleIds.Contains(SmwInstruments.SampleIdOf(position))))
            {
                continue;
            }

            bytes += PlacedBytes(blocks, placedBlocks);
        }

        return bytes;
    }

    // What the song's own samples cost: the blocks of each one where nothing placed before it holds the same blocks.
    private static int OwnSampleBytes(IReadOnlyList<Sample> ownSamples, List<byte[]> placedBlocks)
    {
        int bytes = 0;
        foreach (var sample in ownSamples)
        {
            bytes += PlacedBytes(BrrSample.BlockBytes(sample.FileBytes, sample.FileName), placedBlocks);
        }

        return bytes;
    }

    // Places blocks in ARAM and answers what that costs: their size, or nothing where the same blocks are placed already.
    private static int PlacedBytes(byte[] blocks, List<byte[]> placedBlocks)
    {
        if (placedBlocks.Any(placed => placed.AsSpan().SequenceEqual(blocks)))
        {
            return 0;
        }

        placedBlocks.Add(blocks);
        return blocks.Length;
    }

    // An address rounded up to the next page boundary.
    private static int RoundUpToPage(int address)
    {
        return (address + PageBytes - 1) / PageBytes * PageBytes;
    }
}
