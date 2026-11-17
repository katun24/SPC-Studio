namespace SpcStudio.Import;

// The hot patches a song writes: those in its channels, by tick and channel by channel within one, and those in its remote code blocks.
internal static class SongHotPatches
{
    // The hot patches in a song's channels, by tick, the channels' own order kept within a tick.
    public static List<MmlIgnored> InChannels(MmlFile file)
    {
        return [.. HotPatchesAmong(file.Channels.SelectMany(channel => channel.Events)).OrderBy(patch => patch.Tick)];
    }

    // The hot patches in a song's remote code blocks.
    public static IEnumerable<MmlIgnored> InRemoteBlocks(MmlFile file)
    {
        return HotPatchesAmong(file.RemoteBlocks.Values.SelectMany(block => block));
    }

    // The hot patches among some events.
    private static IEnumerable<MmlIgnored> HotPatchesAmong(IEnumerable<MmlEvent> events)
    {
        return events.OfType<MmlIgnored>().Where(ignored => ignored.IsHotPatch);
    }
}
