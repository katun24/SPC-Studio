namespace SpcStudio.Audio;

// A scheduled change to one channel's volume, v, and any $E8 fade out of it, the byte written only where the event sets its value.
public sealed record ChannelVolumeEvent(long SampleTime, int VoiceIndex, int Value, SequenceFade? Fade = null, bool DoesSetValue = true)
    : SequenceEvent(SampleTime), IChannelEvent;
