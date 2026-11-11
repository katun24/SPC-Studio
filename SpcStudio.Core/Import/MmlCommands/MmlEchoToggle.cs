namespace SpcStudio.Import;

// A $F4 $03, the per-channel echo toggle, which carries no value of its own.
public sealed record MmlEchoToggle(int Tick, int Line) : MmlEvent(Tick, Line);
