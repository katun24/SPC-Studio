namespace SpcStudio.Import;

// The kinds of byte the driver's read-ahead stops at, which is all that decides a byte's cuts.
public enum ReadAheadStop
{
    Tie,
    Note,
    Rest,
    KeyOff,
}
