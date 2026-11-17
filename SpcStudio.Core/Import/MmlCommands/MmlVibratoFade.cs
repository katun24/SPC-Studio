namespace SpcStudio.Import;

// An $EA: how long the vibrato takes to widen from nothing to the extent in force when it runs.
public sealed record MmlVibratoFade(int Tick, int FadeTicks, int Line) : MmlEvent(Tick, Line);
