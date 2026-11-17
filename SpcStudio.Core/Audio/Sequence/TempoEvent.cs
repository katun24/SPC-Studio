namespace SpcStudio.Audio;

// A scheduled change to the song's tempo, t, and any $E3 ramp out of it, the byte written only where the event sets its value.
public sealed record TempoEvent(long SampleTime, int Value, SequenceFade? Fade = null, bool DoesSetValue = true) : SequenceEvent(SampleTime);
