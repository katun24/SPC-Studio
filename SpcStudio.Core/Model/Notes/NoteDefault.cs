namespace SpcStudio.Model;

// The values a note can take from its instrument rather than hold itself: three single numbers, four chains and two flags.
public enum NoteDefault
{
    Gate,
    Velocity,
    FineTune,
    Vibrato,
    Tremolo,
    Bend,
    Legato,
    Hold,
    Envelope,
}
