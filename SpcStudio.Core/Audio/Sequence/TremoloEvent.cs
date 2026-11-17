using SpcStudio.Model;

namespace SpcStudio.Audio;

// A scheduled change to one channel's tremolo, $E5, as the whole state.
public sealed record TremoloEvent(long SampleTime, int VoiceIndex, TremoloState State) : SequenceEvent(SampleTime), IChannelEvent;
