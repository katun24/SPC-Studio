namespace SpcStudio.Import;

// An n, $F8: switch the channel over to the noise generator and set the clock the whole chip runs it at.
public sealed record MmlNoise(int Tick, int Clock, int Line) : MmlEvent(Tick, Line);
