namespace SpcStudio.Audio;

// A scheduled change to the song's song volume, w, and any $E1 fade out of it, the byte written only where the event sets its value.
public sealed record MasterVolumeEvent(long SampleTime, int Value, SequenceFade? Fade = null, bool DoesSetValue = true) : SequenceEvent(SampleTime);
