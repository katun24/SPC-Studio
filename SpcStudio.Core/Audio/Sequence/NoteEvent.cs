namespace SpcStudio.Audio;

// A scheduled key-on on a channel's own voice: what it keys on with, where the note itself ends, and whether it runs into the note before it.
public sealed record NoteEvent(long SampleTime, int VoiceIndex, VoiceKeyOn KeyOn, long EndSample, bool IsLegato = false) : SequenceEvent(SampleTime), IChannelEvent;
