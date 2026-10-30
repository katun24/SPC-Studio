using System.Collections.Immutable;

namespace SpcStudio.Import;

// The rhythm a transcription is placed against: what one step of the song's longest stretch is called, the stretches the song is made of in order, and
// how much of the song sits on its own rhythm, from nothing to all of it.
public readonly record struct SpcGrid(int StepTicks, ImmutableArray<SpcStretch> Stretches, double FitScore)
{
    internal const double TidyWithinSteps = 1 / 6.0;

    // The tempo the song opens at.
    public int BaseTempo
    {
        get
        {
            return Stretches[0].StartTempo;
        }
    }

    // What one step is called where a frame of the dump falls.
    public int StepTicksAt(double frame)
    {
        return StretchAt(frame).StepTicks;
    }

    // Where a frame of the dump falls on the roll exactly, nothing tidied and nothing rounded to a whole tick.
    public double ExactTicksAt(double frame)
    {
        var stretch = StretchAt(frame);
        return stretch.StartTick + stretch.StepsAt(frame) * stretch.StepTicks;
    }

    // Where a frame of the dump falls on the roll, tidied onto a line where it nearly is and rounded to a whole tick.
    public int TidiedTickAt(long frame)
    {
        var stretch = StretchAt(frame);
        int stepTicks = stretch.StepTicks;
        double steps = stretch.StepsAt(frame);
        double nearestLine = Math.Round(steps);
        double exactTicks = stretch.StartTick + steps * stepTicks;
        double tidiedTicks = Math.Abs(steps - nearestLine) <= TidyWithinSteps ? stretch.StartTick + nearestLine * stepTicks : exactTicks;
        return Math.Max(0, (int)Math.Round(tidiedTicks, MidpointRounding.AwayFromZero));
    }

    // The stretch a frame of the dump falls in, each reaching back by the tidying's margin in front of its first line.
    private SpcStretch StretchAt(double frame)
    {
        var stretch = Stretches[0];
        for (int at = 1; at < Stretches.Length; at++)
        {
            var next = Stretches[at];
            if (next.StartFrame - next.PeriodFrames * TidyWithinSteps > frame)
            {
                break;
            }

            stretch = next;
        }

        return stretch;
    }
}
