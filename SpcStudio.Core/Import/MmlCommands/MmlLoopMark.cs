namespace SpcStudio.Import;

// The / loop point.
public sealed record MmlLoopMark(int Tick, int Line) : MmlEvent(Tick, Line);
