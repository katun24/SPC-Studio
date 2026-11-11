namespace SpcStudio.Export;

// One song's ARAM row by row, in the order AddmusicK places it: its compiled data rounded up to a page, the sample table, its sample group's samples,
// how many samples of its own it plays and what they cost, its echo buffer, and what is left, below zero where AddmusicK will refuse it.
public sealed record AramUsage(int SongDataBytes, int SampleTableBytes, int SampleGroupBytes, int SampleCount, int SampleBytes, int EchoBufferBytes, int FreeBytes);
