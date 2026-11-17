using SpcStudio.Audio;

namespace SpcStudio.Export;

// One sample's trip to disk as the export plans it before anything is written: the sample, the file it would go to, and whether a file holding other bytes is already there.
public readonly record struct SampleWrite(Sample Sample, string Path, bool HasClash);
