namespace SpcStudio.Import;

// An $F2, the echo volume fade: one duration and a target for each side.
public sealed record MmlEchoFade(int Tick, int DurationTicks, int TargetLeft, int TargetRight, int Line) : MmlEvent(Tick, Line);
