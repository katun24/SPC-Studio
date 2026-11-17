namespace SpcStudio.Audio;

// A loop a search found: its start point, the boundary past its last point, and the length in points, a fraction of a point included, that its sound
// repeats at.
public readonly record struct FoundLoop(int StartPoint, int EndPoint, double RepeatPoints)
{
    // The loop as one to be fitted to whole blocks.
    public LoopFit Fit
    {
        get
        {
            return new LoopFit(StartPoint, RepeatPoints);
        }
    }
}
