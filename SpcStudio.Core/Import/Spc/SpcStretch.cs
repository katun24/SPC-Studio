namespace SpcStudio.Import;

// One stretch of a song played at one speed, or at a speed changing steadily across the whole of itself: where it starts in the dump and on the roll, how
// long one step of its rhythm lasts there and how fast that speeds up, and how that step is spelled and played.
public readonly record struct SpcStretch(double StartFrame, int StartTick, double PeriodFrames, double GrowthPerFrame, int StepTicks, int StartTempo, int EndTempo)
{
    // Whether the stretch is written as a tempo fade.
    public bool DoesFade
    {
        get
        {
            return EndTempo != StartTempo;
        }
    }

    // How many of the stretch's own steps lie between its start and a frame.
    public double StepsAt(double frame)
    {
        return SpcGridFit.StepsAfter(PeriodFrames, GrowthPerFrame, frame - StartFrame);
    }
}
