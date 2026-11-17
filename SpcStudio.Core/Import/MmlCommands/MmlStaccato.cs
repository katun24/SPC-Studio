namespace SpcStudio.Import;

// A $F4 $02, the light staccato toggle, which carries no value.
public sealed record MmlStaccato(int Tick, int Line) : MmlEvent(Tick, Line);
