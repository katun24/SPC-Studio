namespace SpcStudio.Model;

// Where one bend of a chain is written: the start of the piece in its note the command sits directly behind, counted from the note's start, and the delay and length bytes it carries there.
public readonly record struct BendPlacement(int PieceStartTick, int DelayByte, int LengthByte);
