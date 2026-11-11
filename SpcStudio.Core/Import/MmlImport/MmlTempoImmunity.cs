namespace SpcStudio.Import;

// A $F4 $07, tempo immunity, which takes the time-running-out speed-up off the song's tempo.
public sealed record MmlTempoImmunity(int Tick, int Line) : MmlEvent(Tick, Line);
