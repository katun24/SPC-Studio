namespace SpcStudio.Import;

// Which of the channel messages a MIDI import reads one is.
public enum MidiMessageKind
{
    NoteOff,
    NoteOn,
    ControlChange,
    ProgramChange,
    PitchBend,
}
