using SpcStudio.Model;

namespace SpcStudio.Audio;

// A scheduled change to one channel's envelope registers, $ED.
public sealed record EnvelopeEvent(long SampleTime, int VoiceIndex, EnvelopeState State) : SequenceEvent(SampleTime), IChannelEvent;
