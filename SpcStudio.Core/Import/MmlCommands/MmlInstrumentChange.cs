namespace SpcStudio.Import;

// An @ command, its number as written.
public sealed record MmlInstrumentChange(int Tick, int Number, int Line) : MmlEvent(Tick, Line);
