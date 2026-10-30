namespace SpcStudio.Import;

// One entry in a #samples block: a quoted .brr name, or a #group whose own list of files stands in its place.
public sealed record MmlSampleEntry(string Name, bool IsGroup, int Line);
