namespace SpcStudio.Audio;

// One run of PITCH values none of which was held, inside a block: the value it left and the one it arrived at, the frames it moved over, how many
// values it walked through, and whether it was the value a vibrato swung about that moved.
public sealed record SpcTraceWalk(int From, int To, long Start, long End, int StepCount, bool IsUnderSwing);
