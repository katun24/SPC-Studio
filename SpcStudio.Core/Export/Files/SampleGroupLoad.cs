namespace SpcStudio.Export;

// What one sample group can put into ARAM: the sample table entries for all of its entries, and each entry's block data, null where the disk does not hold
// it, with whether the entry is marked important, by its position in the group.
public sealed record SampleGroupLoad(int TableBytes, IReadOnlyList<(byte[]? Blocks, bool IsImportant)> SamplesByPosition);
