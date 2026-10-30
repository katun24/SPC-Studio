namespace SpcStudio.Import;

// An $EE: the channel's fine tuning from here on, which each note keys on under.
public sealed record MmlFineTune(int Tick, int Value, int Line) : MmlEvent(Tick, Line);
