using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Cuts off the part of each sample that plays once which the song can never reach, for the export: the song is played through as the ROM plays it, and
// each such sample is written cut a tick and a block past the furthest point any voice read it, into a folder of its own beside the whole one.
public static class SampleTrim
{
    public const string Folder = "trimmed";
    private const string FolderPrefix = Folder + "/";
    private const int MarginBlocks = 1;

    // The song the export writes under the options: the song itself, or a copy of it whose samples that play once are cut short.
    public static Song SongToExport(Song song, MmlExportOptions options)
    {
        return options.TrimOneShotSamples ? Trimmed(song, options) : song;
    }

    // A copy of the song with every sample it plays once cut short of what the song exported under the options never reaches, renamed into the trimmed
    // folder, a sample with nothing to cut left as it was.
    public static Song Trimmed(Song song, MmlExportOptions options)
    {
        var reachLog = ReachLogOf(song, options);
        double tickMarginSamples = TempoMap.For(song).LongestTickSeconds() * DspPoint.SampleRate;
        var trimmed = song.Copy();

        foreach (var sample in SampleExport.UsedSamples(song).Where(sample => !sample.Decoded.DoesLoop))
        {
            int keptBlockCount = KeptBlockCount(sample, reachLog.ReachOf(sample.Decoded), tickMarginSamples);
            if (keptBlockCount < sample.BlockCount)
            {
                trimmed.ReplaceSample(CutSample(sample, keptBlockCount));
            }
        }

        return trimmed;
    }

    // The name a sample file had before the trim renamed it into the trimmed folder, or null where it is not in that folder.
    public static string? UntrimmedFileName(string fileName)
    {
        string forwardSlashed = fileName.Replace('\\', '/');
        return forwardSlashed.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase) ? forwardSlashed[FolderPrefix.Length..] : null;
    }

    // How far the song's voices read each sample, played as the ROM plays its export under the options: from the start through its loop twice, and on
    // until the last release ends.
    private static SampleReachLog ReachLogOf(Song song, MmlExportOptions options)
    {
        var (events, lengthSamples) = SequenceBuilder.BuildIgnoringMuting(song, options, song.EndTick);
        long? loopStartSample = song.IsLooping ? TempoMap.For(song).SampleAt(song.LoopTick) : null;
        long secondPassSamples = loopStartSample is long loopStart ? Math.Max(0, lengthSamples - loopStart) : 0;

        var reachLog = new SampleReachLog();
        var mixer = new BrrMixer((int)DspPoint.SampleRate);
        mixer.LogSampleReach(reachLog);
        mixer.StartSequence(events, lengthSamples, 0, loopStartSample);
        mixer.ReadFrames(lengthSamples + secondPassSamples + SnesEnvelope.LongestReleaseSamples, (_, _, _) => { });
        return reachLog;
    }

    // How many of a sample's blocks are kept: up to the one holding the furthest point read plus a tick at the highest pitch it was read at, and a block
    // more - all of them where no voice read it, there being nothing measured to cut it by.
    private static int KeptBlockCount(Sample sample, SampleReachLog.SampleReach? reach, double tickMarginSamples)
    {
        if (reach is not { } read)
        {
            return sample.BlockCount;
        }

        long tickMarginPoints = (long)Math.Ceiling(tickMarginSamples * read.HighestPitchRegister / SnesPitch.UnityPitchRegister);
        long lastReachablePoint = read.FurthestPoint + tickMarginPoints;
        return (int)Math.Min(lastReachablePoint / BrrSample.PointsPerBlock + 1 + MarginBlocks, sample.BlockCount);
    }

    // A sample cut off after a number of blocks and renamed into the trimmed folder, what it plays decoded again.
    private static Sample CutSample(Sample sample, int keptBlockCount)
    {
        byte[] cutBytes = BrrSample.WithBlocksKept(sample.FileBytes, keptBlockCount, sample.FileName);
        return sample with { FileName = FolderPrefix + sample.FileName, FileBytes = cutBytes, Decoded = BrrSample.Decode(cutBytes, sample.FileName) };
    }
}
