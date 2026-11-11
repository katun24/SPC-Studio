namespace SpcStudio.Import;

// An $F5: the eight FIR coefficients it writes, packed the way the echo's own filter table holds them.
public sealed record MmlFirFilter(int Tick, long PackedCoefficients, int Line) : MmlEvent(Tick, Line);
