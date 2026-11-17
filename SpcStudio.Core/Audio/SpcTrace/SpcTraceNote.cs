namespace SpcStudio.Audio;

// One pitch a voice settled on and held inside a block: the PITCH register, and the frames from where it took over to where the next one did.
public sealed record SpcTraceNote(int Pitch, long Start, long End);
