using SpcStudio.Model;

namespace SpcStudio.Import;

// One #instruments entry: what it plays - a sample, the noise generator, a built-in's sample or a position in the sample table - and its five bytes
// exactly as written.
public sealed record MmlInstrument(string? FileName, int? NoiseClock, int? SampleCopiedFrom, InstrumentBytes Bytes, int Line)
{
    // The position in the sample table an AddMusic4 instrument table entry plays, or null where the entry names its sample another way.
    public int? SamplePosition { get; init; }
}
