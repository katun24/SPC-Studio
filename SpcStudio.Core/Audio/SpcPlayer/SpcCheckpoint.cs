namespace SpcStudio.Audio;

// One saved chip state, as a whole .spc dump, and the frame of the song it was taken at.
public sealed record SpcCheckpoint(long Frame, byte[] State);
