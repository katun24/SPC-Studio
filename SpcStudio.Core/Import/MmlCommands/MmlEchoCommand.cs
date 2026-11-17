namespace SpcStudio.Import;

// One of the three echo commands - $EF, $F1 or $F0 - with its three argument bytes, zero where it takes none.
public sealed record MmlEchoCommand(int Tick, int CommandByte, int FirstArgument, int SecondArgument, int ThirdArgument, int Line) : MmlEvent(Tick, Line);
