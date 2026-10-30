namespace SpcStudio.Export;

// What one sample group puts into ARAM: the sample table entries for all of its entries, and the block data of each entry marked important that the disk holds.
public sealed record SampleGroupLoad(int TableBytes, IReadOnlyList<byte[]> ImportantSampleBlocks);
