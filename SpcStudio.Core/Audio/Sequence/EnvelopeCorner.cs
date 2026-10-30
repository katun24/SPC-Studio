namespace SpcStudio.Audio;

// A corner of an envelope's shape: the level reached, the phase it was reached in, and when, counted from key-on.
public readonly record struct EnvelopeCorner(double Seconds, int Level, EnvelopePhase Phase);
