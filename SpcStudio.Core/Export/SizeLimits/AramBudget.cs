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
        if (string.IsNullOrEmpty(amkRoot))
        {
            return null;
        }

        if (ReadDriverHeader(Path.Combine(amkRoot, DriverPath)) is not { } header)
        {
            return null;
        }

        int programBytes = BinaryPrimitives.ReadUInt16LittleEndian(header);
        int programAddress = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(ProgramAddressOffset));
        int availableBytes = SpcReader.AramSize - programBytes - programAddress;

        return availableBytes > 0 ? availableBytes : null;
    }

    // Reads the budget and what every sample group loads out of the install, or null where the budget cannot be read.
    public static AramInstall? ReadInstall(string? amkRoot)
    {
        if (AvailableBytes(amkRoot) is not { } availableBytes)
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

        return new AramInstall(availableBytes, groups, loadByGroup);
    }

    // What the song costs in each part of ARAM against the install, given its compiled data's size.
    public static AramUsage Usage(Song song, AramInstall install, int songDataBytes)
    {
        int songStart = SpcReader.AramSize - install.AvailableBytes;
        int roundedSongDataBytes = RoundUpToPage(songStart + songDataBytes) - songStart;

        var ownSamples = SampleExport.UsedSamples(song);
        var placedBlocks = new List<byte[]>();

        // The sample group the song loads, AddmusicK's default where it names neither a group nor a sample of its own.
        string? loadedGroup = song.SampleGroup ?? (ownSamples.Count == 0 ? SampleGroups.DefaultGroup : null);

        int groupBytes = loadedGroup is not null && install.LoadBySampleGroup.TryGetValue(loadedGroup, out var groupLoad) ? SampleGroupBytes(groupLoad, placedBlocks) : 0;
        int sampleBytes = OwnSampleBytes(ownSamples, placedBlocks);

        int echoBytes = Math.Max(EchoBufferBytes(song), install.GlobalSongsEchoBufferBytes);
        int freeBytes = SpcReader.AramSize - echoBytes - (songStart + roundedSongDataBytes + groupBytes + sampleBytes);

        return new AramUsage(roundedSongDataBytes, ownSamples.Count, sampleBytes, groupBytes, echoBytes, freeBytes);
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

        return songList.GlobalSongs.Values.Select(listName => ImportedEchoBufferBytes(AmkSongList.SongPath(amkRoot, listName))).DefaultIfEmpty(0).Max();
    }

    // What the echo buffer of the song an MML file holds costs, or nothing where the file cannot be imported.
    private static int ImportedEchoBufferBytes(string mmlPath)
    {
        try
        {
            return EchoBufferBytes(MmlImporter.Load(mmlPath));
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // The first bytes of the compiled driver, or null where the file cannot be read or is shorter than its header.
    private static byte[]? ReadDriverHeader(string driverPath)
    {
        return ReadableFiles.BytesOrNull(driverPath) is { Length: >= DriverHeaderBytes } driverBytes ? driverBytes[..DriverHeaderBytes] : null;
    }

    // What a sample group loads for a song that names none of its slots: a table entry for each entry, and the blocks of each one marked important.
    private static SampleGroupLoad SampleGroupLoadOf(SampleGroup group, string samplesFolder)
    {
        var importantSampleBlocks = new List<byte[]>();

        foreach (var entry in group.Entries.Where(entry => entry.IsImportant))
        {
            if (ReadableFiles.BytesOrNull(Path.Combine(samplesFolder, entry.Path)) is not { } fileBytes)
            {
                continue;
            }

            try
            {
                importantSampleBlocks.Add(BrrSample.BlockBytes(fileBytes, entry.Path));
            }
            catch (InvalidDataException)
            {
            }
        }

        return new SampleGroupLoad(group.Entries.Count * Sample.SampleTableBytes, importantSampleBlocks);
    }

    // What the loaded group costs: its table entries, and the blocks of its important samples where no earlier one holds the same blocks.
    private static int SampleGroupBytes(SampleGroupLoad groupLoad, List<byte[]> placedBlocks)
    {
        int bytes = groupLoad.TableBytes;
        foreach (var blocks in groupLoad.ImportantSampleBlocks)
        {
            bytes += PlacedBytes(blocks, placedBlocks);
        }

        return bytes;
    }

    // What the song's own samples cost: a table entry each, and the blocks of each one where nothing placed before it holds the same blocks.
    private static int OwnSampleBytes(IReadOnlyList<Sample> ownSamples, List<byte[]> placedBlocks)
    {
        int bytes = 0;
        foreach (var sample in ownSamples)
        {
            bytes += Sample.SampleTableBytes + PlacedBytes(BrrSample.BlockBytes(sample.FileBytes, sample.FileName), placedBlocks);
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
