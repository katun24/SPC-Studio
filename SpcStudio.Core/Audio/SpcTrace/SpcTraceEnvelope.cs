using SpcStudio.Model;

namespace SpcStudio.Audio;

// One set of envelope registers a voice held inside a block, and the frame it took them up at.
public readonly record struct SpcTraceEnvelope(long Frame, EnvelopeState State);
