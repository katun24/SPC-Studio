using SpcStudio.Model;

namespace SpcStudio.Audio;

// Envelope registers taking over at a time counted from key-on, the first of a run at key-on itself.
public readonly record struct EnvelopeChange(double Seconds, EnvelopeState Registers);
