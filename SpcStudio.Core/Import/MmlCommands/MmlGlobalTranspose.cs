namespace SpcStudio.Import;

// An $E4, the song's semitone transpose, which every note keyed on after it plays under whichever channel wrote it.
public sealed record MmlGlobalTranspose(int Tick, int Semitones, int Line) : MmlEvent(Tick, Line);
