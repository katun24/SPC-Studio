namespace SpcStudio.Import;

// An $E5 or an $FD: the tremolo's delay, rate and amount, an $FD arriving as an amount of zero with the other two left as they were.
public sealed record MmlTremolo(int Tick, int? Delay, int? Rate, int Amount, int Line) : MmlEvent(Tick, Line);
