namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds what the passes writing runs of notes under remote blocks share: finding the runs, dropping
// the blocks that do not pay for themselves, and numbering the rest.
public static partial class MmlExporter
{
    // Every channel's body with the runs of remote blocks armed, and the blocks themselves: each channel's header and body parsed in the order written,
    // the labels a channel defines followed into the channels after it, its runs planned, the runs of every block not paying for itself dropped, the
    // blocks numbered from the first number in the order first armed, and each channel written with its runs.
    private static (List<string> Bodies, List<string> Definitions) ChannelsWithBlockRuns<TChannel, TRun>(
        IReadOnlyList<(string Header, string Body)> channels, int firstNumber, Func<List<string>, List<string>, KeyOnBlocks.LabelStatesLeft, TChannel> parseChannel,
        Func<TChannel, List<TRun>> planRuns, Func<TChannel, List<TRun>, Dictionary<string, int>, string> writeChannel) where TRun : class, IBlockRun
    {
        var labelStatesLeft = new KeyOnBlocks.LabelStatesLeft();
        var parsedChannels = channels.Select(channel => parseChannel(WrittenTokens.Split(channel.Header), WrittenTokens.Split(channel.Body), labelStatesLeft)).ToList();
        var runsByChannel = RunsOfPayingBlocks(parsedChannels.Select(planRuns));
        var (numbersByBlock, definitions) = NumberedBlocks(runsByChannel.SelectMany(runs => runs), firstNumber);
        var bodies = parsedChannels.Select((channel, channelIndex) => writeChannel(channel, runsByChannel[channelIndex], numbersByBlock)).ToList();

        return (bodies, definitions);
    }

    // The runs a channel's key-ons are worth arming, chosen from the first key-on on: the best run starting at each where it saves anything, the next
    // one looked for past its last key-on.
    private static List<TRun> PlannedRuns<TRun>(int keyOnCount, Func<int, TRun?> bestRunFrom, Func<TRun, int> lastKeyOnOf) where TRun : class, IBlockRun
    {
        var runs = new List<TRun>();

        for (int keyOnIndex = 0; keyOnIndex < keyOnCount;)
        {
            if (bestRunFrom(keyOnIndex) is { NetBytesSaved: > 0 } bestRun)
            {
                runs.Add(bestRun);
                keyOnIndex = lastKeyOnOf(bestRun) + 1;
            }
            else
            {
                keyOnIndex++;
            }
        }

        return runs;
    }

    // The last key-on of the run starting at a first one that a test lets in, each later key-on in the first's stretch of the channel, and the bytes the
    // run's key-ons save together, the test answering what a key-on saves or null where it cannot join.
    private static (int LastKeyOn, int BytesSaved) RunExtent<TKeyOn>(IReadOnlyList<TKeyOn> keyOns, int firstKeyOn, Func<TKeyOn, int> segmentOf, Func<TKeyOn, int?> bytesSavedAt)
    {
        int lastKeyOn = firstKeyOn;
        int bytesSaved = bytesSavedAt(keyOns[firstKeyOn]) ?? throw new InvalidOperationException("A run cannot start at a key-on that cannot join it.");

        while (lastKeyOn + 1 < keyOns.Count
               && segmentOf(keyOns[lastKeyOn + 1]) == segmentOf(keyOns[firstKeyOn])
               && bytesSavedAt(keyOns[lastKeyOn + 1]) is int moreBytesSaved)
        {
            lastKeyOn++;
            bytesSaved += moreBytesSaved;
        }

        return (lastKeyOn, bytesSaved);
    }

    // Each channel's runs, the runs of every block dropped whose runs together save no more than writing the block once costs - its commands and the
    // byte that closes it.
    private static List<List<TRun>> RunsOfPayingBlocks<TRun>(IEnumerable<IEnumerable<TRun>> runsByChannel) where TRun : IBlockRun
    {
        var runLists = runsByChannel.Select(runs => runs.ToList()).ToList();
        var unpaidBlocks = runLists.SelectMany(runs => runs)
            .GroupBy(run => run.BlockText)
            .Where(group => group.Sum(run => run.NetBytesSaved) <= WrittenTokens.Split(group.Key).Sum(WrittenTokens.CompiledBytesOf) + SongDataSize.LoopEndBytes)
            .Select(group => group.Key)
            .ToHashSet();

        return [.. runLists.Select(runs => runs.Where(run => !unpaidBlocks.Contains(run.BlockText)).ToList())];
    }

    // The number of each block the runs arm, numbered from the first number in the order they are first armed, and the blocks' definitions in that order.
    private static (Dictionary<string, int> NumbersByBlock, List<string> Definitions) NumberedBlocks(IEnumerable<IBlockRun> runs, int firstNumber)
    {
        var numbersByBlock = new Dictionary<string, int>();
        var definitions = new List<string>();
        foreach (string blockText in runs.Select(run => run.BlockText).Distinct())
        {
            int blockNumber = firstNumber + definitions.Count;
            numbersByBlock[blockText] = blockNumber;
            definitions.Add(RemoteBlockDefinition(blockNumber, blockText));
        }

        return (numbersByBlock, definitions);
    }

    // A run of notes one remote block is armed over: what tells its block apart, and the bytes it saves once its own arm and clear are paid for.
    private interface IBlockRun
    {
        string BlockText { get; }
        int NetBytesSaved { get; }
    }
}
