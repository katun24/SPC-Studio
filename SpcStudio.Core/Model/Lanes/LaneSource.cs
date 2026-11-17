namespace SpcStudio.Model;

// Where one of a note's values comes from, for a value with a lane beneath it: the lane, the note's instrument, or the note's own value.
public enum LaneSource
{
    None,
    Instrument,
    Note,
}
