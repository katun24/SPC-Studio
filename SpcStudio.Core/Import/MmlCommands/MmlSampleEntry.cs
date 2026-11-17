namespace SpcStudio.Import;

// One entry in a #samples block: a quoted .brr name, or a #group whose own list of files stands in its place.
public sealed record MmlSampleEntry(string Name, bool IsGroup, int Line)
{
    // The quoted .brr names among a #samples block's entries, which is what a song loads of its own.
    public static IEnumerable<string> FileNamesIn(IEnumerable<MmlSampleEntry> entries)
    {
        return entries.Where(entry => !entry.IsGroup).Select(entry => entry.Name);
    }
}
