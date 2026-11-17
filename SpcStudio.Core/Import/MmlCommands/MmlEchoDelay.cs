namespace SpcStudio.Import;

// A $FA $04, the echo delay written on its own.
public sealed record MmlEchoDelay(int Tick, int Delay, int Line) : MmlEvent(Tick, Line);
