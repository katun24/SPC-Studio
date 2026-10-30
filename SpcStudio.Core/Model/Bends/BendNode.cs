namespace SpcStudio.Model;

// One joint of a note's $DD chain as the roll edits it: a tick from the note's own start and the pitch the note is sounding at there, in semitones above C4.
public readonly record struct BendNode(int Tick, int Semitone);
