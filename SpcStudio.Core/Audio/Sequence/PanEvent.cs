using SpcStudio.Model;

namespace SpcStudio.Audio;

// A scheduled change to one channel's pan, y, with the surround pair the same command writes and any $DC fade out of it, the byte written only where the event sets its value.
public sealed record PanEvent(long SampleTime, int VoiceIndex, int Value, SnesSurround Surround = default, SequenceFade? Fade = null, bool DoesSetValue = true)
    : SequenceEvent(SampleTime), IChannelEvent;
