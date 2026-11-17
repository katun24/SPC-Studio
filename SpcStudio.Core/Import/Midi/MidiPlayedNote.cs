namespace SpcStudio.Import;

// One note a MIDI channel sounded: its channel counted from 0, its key and velocity, the program in force when it was struck, and the MIDI ticks it
// sounded from and to, the sustain pedal's hold included.
public readonly record struct MidiPlayedNote(int Channel, int Key, int Velocity, int Program, long StartTick, long EndTick);
