namespace SpcStudio.Import;

// One entry of a sample group: its path under AddmusicK's samples folder, and whether a '!' marks it as a sample that stays in ARAM whatever the song uses.
public sealed record SampleGroupEntry(string Path, bool IsImportant);
