namespace SpcStudio.Import;

// The restore a remote code type 5 or 6 makes at each key-on, the voice's envelope registers put back to what the backup table holds.
public sealed record MmlKeyOnRestore(int Tick, int Line) : MmlEvent(Tick, Line);
