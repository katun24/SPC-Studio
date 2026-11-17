namespace SpcStudio.Audio;

// One stretch of a register of a block swinging evenly about one value at one width: where its sweep set off from that value, where it stopped, the
// value it swung about, how long one cycle of it took, how far it reached either side in its scale's units, and where it first did.
public sealed record SpcTraceSwing(long Start, long End, int Center, double CycleFrames, double Peak, long FullWidthFrame)
{
    // Whether a frame lies inside the stretch.
    public bool IsSwingingAt(long frame)
    {
        return frame >= Start && frame < End;
    }
}
