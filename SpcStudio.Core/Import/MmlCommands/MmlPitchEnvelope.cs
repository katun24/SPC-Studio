namespace SpcStudio.Import;

// An $EB, $EC or $FE: the channel's pitch envelope, which puts a bend on every note that follows - an attack or a release, its delay, its ramp length, a length of zero being off, and its extent.
public sealed record MmlPitchEnvelope(int Tick, bool IsAttack, int DelayTicks, int LengthTicks, int Extent, int Line) : MmlEvent(Tick, Line);
