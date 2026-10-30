namespace SpcStudio.Model;

// The whole of one channel's vibrato at a moment: the three bytes one $DE writes, and the fade an $EA after it adds.
public readonly record struct VibratoState(int Delay, int Rate, int Extent, int FadeTicks = SnesVibrato.MinFadeTicks) : ISweepState<VibratoState>
{
    // A channel with no vibrato, which is what the song init leaves.
    public static VibratoState Off
    {
        get
        {
            return new(SnesSweep.DefaultDelay, SnesSweep.DefaultRate, SnesVibrato.DefaultExtent, SnesVibrato.MinFadeTicks);
        }
    }

    // Whether the sweep widens into its extent rather than starting at it.
    public bool HasFade
    {
        get
        {
            return FadeTicks != SnesVibrato.MinFadeTicks;
        }
    }

    // Whether this modulates anything, which the extent alone decides.
    public bool IsActive
    {
        get
        {
            return Extent != SnesVibrato.MinExtent;
        }
    }

    // This state as a voice can be heard playing it: itself where it sweeps, and no vibrato where it does not.
    public VibratoState Audible()
    {
        return IsActive ? this : Off;
    }

    // This state with its delay byte replaced and its other values untouched.
    public VibratoState WithDelay(int delay)
    {
        return this with { Delay = delay };
    }
}
