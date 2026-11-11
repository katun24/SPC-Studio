namespace SpcStudio.Import;

// A p, a $DE or a $DF: the vibrato's delay, rate and extent, a $DF arriving as an extent of zero with the other two left as they were.
public sealed record MmlVibrato(int Tick, int? Delay, int? Rate, int Extent, int Line) : MmlEvent(Tick, Line);
