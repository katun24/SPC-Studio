namespace SpcStudio.Import;

// A $FA $03, the amplify, and the value it sets.
public sealed record MmlAmplify(int Tick, int Value, int Line) : MmlEvent(Tick, Line);
