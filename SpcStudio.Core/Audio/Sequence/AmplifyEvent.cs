namespace SpcStudio.Audio;

// A scheduled change to one channel's amplify, $FA $03.
public sealed record AmplifyEvent(long SampleTime, int VoiceIndex, int Value) : SequenceEvent(SampleTime), IChannelEvent;
