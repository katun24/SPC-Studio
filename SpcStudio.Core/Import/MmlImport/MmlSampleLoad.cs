namespace SpcStudio.Import;

// An $F3, the sample load: the channel's sample - by its place in the #samples block, or by file name - and the integer half of its tuning.
public sealed record MmlSampleLoad(int Tick, int SampleNumber, string? FileName, int TuningHigh, int Line) : MmlEvent(Tick, Line);
