namespace SpcStudio.Import;

// One tempo a MIDI file sets: the tick it takes over at and how long a quarter note lasts from there.
public readonly record struct MidiTempoChange(long Tick, int MicrosecondsPerQuarterNote);
