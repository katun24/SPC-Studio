using SpcStudio.Model;

namespace SpcStudio.Audio;

// A scheduled change to the song's echo - $EF, $F1 or $F0 - as the whole state, whether the two echo volumes are written here, and the $F2 fade each side arms.
public sealed record EchoEvent(long SampleTime, EchoState State, bool DoesSetVolume = true, SequenceFade? FadeLeft = null, SequenceFade? FadeRight = null)
    : SequenceEvent(SampleTime);
