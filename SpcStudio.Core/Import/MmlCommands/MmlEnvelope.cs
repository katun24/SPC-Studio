namespace SpcStudio.Import;

// An $ED, the envelope override, as its own two argument bytes.
public sealed record MmlEnvelope(int Tick, int FirstArgument, int SecondArgument, int Line) : MmlEvent(Tick, Line);
