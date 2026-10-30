namespace SpcStudio.Import;

// Which of the driver's two read-aheads a song is read under - the one that steps over loops, or the one a hot patch sends inside them - and the song either reader makes of it under that one.
public static class ReadAheadPatch
{
    // The song a reader makes of the text or the bytes under the read-ahead it plays under, the reader being told whether that one looks inside loops.
    public static MmlFile ReadUnderPatch(Func<bool, MmlFile> read)
    {
        var (plainReading, plainRefusal) = AttemptReading(read, false);
        if (plainReading is not null && !DoesAnyPatchReadIntoLoops(plainReading))
        {
            return plainReading;
        }

        var (patchedReading, patchedRefusal) = AttemptReading(read, true);
        var eitherReading = plainReading ?? patchedReading ?? throw plainRefusal!;
        var (doesStartInsideLoops, firstChange) = PatchState(eitherReading);
        if (firstChange is null)
        {
            return doesStartInsideLoops ? patchedReading ?? throw patchedRefusal! : plainReading ?? throw plainRefusal!;
        }

        if (plainReading is not null && patchedReading is not null && DoReadingsAgree(plainReading, patchedReading))
        {
            return plainReading;
        }

        throw MmlImportException.HotPatchesDisagreeOnReadAhead(firstChange.Line);
    }

    // One reading with its loop-edge ties decided, or the refusal that stopped it.
    private static (MmlFile? Reading, MmlImportException? Refusal) AttemptReading(Func<bool, MmlFile> read, bool doesReadIntoLoops)
    {
        try
        {
            return (LoopEdgeTies.Resolve(read(doesReadIntoLoops)), null);
        }
        catch (MmlImportException refusal)
        {
            return (null, refusal);
        }
    }

    // The hot patches among some events.
    private static IEnumerable<MmlIgnored> HotPatches(IEnumerable<MmlEvent> events)
    {
        return events.OfType<MmlIgnored>()
            .Where(ignored => ignored.Command is IgnoredCommand.HotPatchPreset or IgnoredCommand.HotPatchBits or IgnoredCommand.ReadAheadHotPatch);
    }

    // Every event in the song's remote code blocks.
    private static IEnumerable<MmlEvent> RemoteBlockEvents(MmlFile file)
    {
        return file.RemoteBlocks.Values.SelectMany(block => block);
    }

    // Whether any patch in the song sends the read-ahead inside loops.
    private static bool DoesAnyPatchReadIntoLoops(MmlFile file)
    {
        return HotPatches(file.Channels.SelectMany(channel => channel.Events).Concat(RemoteBlockEvents(file))).Any(patch => patch.DoesReadIntoLoops);
    }

    // Whether the song starts with the read-ahead inside loops, and the first patch that says otherwise.
    private static (bool DoesStartInsideLoops, MmlIgnored? FirstChange) PatchState(MmlFile file)
    {
        var channelHotPatches = file.Channels.SelectMany(channel => HotPatches(channel.Events)).OrderBy(patch => patch.Tick).ToList();
        bool doesStartInsideLoops = channelHotPatches.FirstOrDefault(patch => patch.Tick == 0)?.DoesReadIntoLoops ?? false;
        var firstChangingPatch = channelHotPatches.Concat(HotPatches(RemoteBlockEvents(file))).FirstOrDefault(patch => patch.DoesReadIntoLoops != doesStartInsideLoops);
        return (doesStartInsideLoops, firstChangingPatch);
    }

    // Whether two readings make one song: the same notes, and every tie and loop edge read alike.
    private static bool DoReadingsAgree(MmlFile firstReading, MmlFile secondReading)
    {
        if (firstReading.Channels.Count != secondReading.Channels.Count)
        {
            return false;
        }

        return firstReading.Channels.Zip(secondReading.Channels).All(pair =>
            NoteShapes(pair.First).SequenceEqual(NoteShapes(pair.Second))
            && pair.First.CutTies.SequenceEqual(pair.Second.CutTies)
            && EdgesAsCutsSeeThem(pair.First).SequenceEqual(EdgesAsCutsSeeThem(pair.Second))
            && pair.First.RestBytes.SequenceEqual(pair.Second.RestBytes)
            && pair.First.RestTies.SequenceEqual(pair.Second.RestTies));
    }

    // What a channel's notes are, for comparing two readings: where each starts, how long it is, whether it is held, and its byte layout.
    private static IEnumerable<(int Tick, int LengthTicks, bool IsHeld, string Pieces)> NoteShapes(MmlChannel channel)
    {
        return channel.Events.OfType<MmlNote>().Select(note => (note.Tick, note.LengthTicks, note.IsHeld, string.Join(",", note.Pieces)));
    }

    // A channel's loop edges with a key-off read as the note it is to the cuts.
    private static IEnumerable<MmlLoopEdge> EdgesAsCutsSeeThem(MmlChannel channel)
    {
        return channel.LoopEdges.Select(edge => edge.ReadAheadStop == ReadAheadStop.KeyOff ? edge with { ReadAheadStop = ReadAheadStop.Note } : edge);
    }
}
