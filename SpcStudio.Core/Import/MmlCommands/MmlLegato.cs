namespace SpcStudio.Import;

// A $F4 $01, the legato toggle, which carries no value.
public sealed record MmlLegato(int Tick, int Line) : MmlEvent(Tick, Line);
