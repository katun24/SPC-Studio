namespace SpcStudio.Import;

// One channel message of a MIDI file: the tick it plays at, its channel counted from 0, what kind it is and its one or two data values, a pitch bend's
// two halves joined into one.
public readonly record struct MidiMessage(long Tick, int Channel, MidiMessageKind Kind, int FirstValue, int SecondValue);
