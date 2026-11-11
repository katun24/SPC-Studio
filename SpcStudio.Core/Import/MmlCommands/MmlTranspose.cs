namespace SpcStudio.Import;

// A $FA $02, the channel's semitone transpose, its byte read as signed.
public sealed record MmlTranspose(int Tick, int Semitones, int Line) : MmlEvent(Tick, Line);
