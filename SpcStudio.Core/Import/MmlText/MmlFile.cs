using SpcStudio.Model;

namespace SpcStudio.Import;

// What an AddmusicK song reads as before anything is fitted to a song of ours: its directives, and each written channel as a stream of events at ticks.
public sealed record MmlFile(
    string SamplePath,
    IReadOnlyList<MmlSampleEntry> Samples,
    IReadOnlyList<MmlInstrument> Instruments,
    bool DoesLoop,
    bool HasTempoImmunity,
    IReadOnlyList<MmlChannel> Channels,
    IReadOnlyDictionary<int, IReadOnlyList<MmlEvent>> RemoteBlocks)
{
    // The #spc block's four text fields, empty where the song states none.
    public SpcTags SpcTags { get; init; } = SpcTags.Empty;

    // Whether the song was read under the hot patch that sends the read-ahead inside loops, so that every loop edge finds the byte played next.
    public bool DoesReadIntoLoops { get; init; }

    // The quoted .brr names, which is what a song loads of its own.
    public IEnumerable<string> SampleFiles
    {
        get
        {
            return Samples.Where(entry => !entry.IsGroup).Select(entry => entry.Name);
        }
    }
}
