namespace SpcStudio.Model;

// One vertex of a bend path: a tick from the note's own start, and the pitch the note is sounding at there in semitones above C4.
public readonly record struct BendPoint(int Tick, int Semitone);
