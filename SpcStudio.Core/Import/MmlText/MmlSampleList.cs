namespace SpcStudio.Import;

// The samples a song loads of its own: its #path, empty where it states none, and its #samples entries in the order they are written.
internal sealed record MmlSampleList(string SamplePath, IReadOnlyList<MmlSampleEntry> Samples);
