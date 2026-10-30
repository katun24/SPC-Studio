namespace SpcStudio.Model;

// Where one of a note's own values comes from, for a value with no lane beneath it: the note itself, or its instrument.
public enum NoteSource
{
    Note,
    Instrument,
}
