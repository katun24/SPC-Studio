namespace SpcStudio.Import;

// A $FA $01, the GAIN command, and the byte it writes.
public sealed record MmlEnvelopeGain(int Tick, int Gain, int Line) : MmlEvent(Tick, Line);
