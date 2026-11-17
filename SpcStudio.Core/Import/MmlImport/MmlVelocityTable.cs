namespace SpcStudio.Import;

// A switch of the velocity table q is read off: true for the row the game shipped with, false for the N-SPC one.
public sealed record MmlVelocityTable(int Tick, bool UsesSmwVelocities, int Line) : MmlEvent(Tick, Line);
