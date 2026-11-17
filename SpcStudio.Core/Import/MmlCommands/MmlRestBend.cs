namespace SpcStudio.Import;

// A $DD written behind a rest byte, or behind a tie with no note sounding in front of it: the tick that byte starts on, and the command's three bytes.
public sealed record MmlRestBend(int Tick, int DelayTicks, int LengthTicks, int TargetNoteValue, int Line) : MmlEvent(Tick, Line);
