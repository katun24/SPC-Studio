using SpcStudio.Model;

namespace SpcStudio.Audio;

// A scheduled change to one channel's vibrato, p, as the whole state.
public sealed record VibratoEvent(long SampleTime, int VoiceIndex, VibratoState State) : SequenceEvent(SampleTime), IChannelEvent;
