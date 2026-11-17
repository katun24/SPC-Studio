namespace SpcStudio.Import;

// A $F4 $09, the instrument restore that ends an envelope override.
public sealed record MmlEnvelopeRestore(int Tick, int Line) : MmlEvent(Tick, Line);
