using SpcStudio.Model;

namespace SpcStudio.Import;

// One #instruments entry: what it plays - a sample, the noise generator or a built-in's sample - and its five bytes exactly as written.
public sealed record MmlInstrument(string? FileName, int? NoiseClock, int? SampleCopiedFrom, InstrumentBytes Bytes, int Line);
