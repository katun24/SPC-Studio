namespace SpcStudio.Audio;

// Something the sequencer does at a moment, timed in 32 kHz samples from the start of the sequence.
public abstract record SequenceEvent(long SampleTime);
