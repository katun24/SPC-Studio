namespace SpcStudio.Export;

// One song's ARAM row by row: its compiled data rounded up to a page, how many samples of its own it plays and what they cost with their table entries,
// its sample group, its echo buffer, and what is left, below zero where AddmusicK will refuse it.
public sealed record AramUsage(int SongDataBytes, int SampleCount, int SampleBytes, int SampleGroupBytes, int EchoBufferBytes, int FreeBytes);
