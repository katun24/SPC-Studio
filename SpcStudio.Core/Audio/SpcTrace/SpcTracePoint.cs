namespace SpcStudio.Audio;

// One value one of a voice's registers took up inside a block, and the frame it took it up at.
public readonly record struct SpcTracePoint(long Frame, int Value);
