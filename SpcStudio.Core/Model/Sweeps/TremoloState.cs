namespace SpcStudio.Model;

// The whole of one channel's tremolo at a moment: the three bytes one $E5 writes.
public readonly record struct TremoloState(int Delay, int Rate, int Amount) : ISweepState<TremoloState>
{
    private const int DelayTicksCountedOnKeyOnTick = 1;

    // A channel with no tremolo, which is what the song init leaves.
    public static TremoloState Off
    {
        get
        {
            return new(SnesSweep.DefaultDelay, SnesSweep.DefaultRate, SnesTremolo.DefaultAmount);
        }
    }

    // Whether this modulates anything, which the amount alone decides.
    public bool IsActive
    {
        get
        {
            return Amount != SnesTremolo.MinAmount;
        }
    }

    // How many ticks a voice under this tremolo goes after its key-on without ducking, which is its delay less the key-on's own tick.
    public int UnheardTicksAfterKeyOn
    {
        get
        {
            return Delay - DelayTicksCountedOnKeyOnTick;
        }
    }

    // This state as a voice can be heard playing it: itself where it ducks, and no tremolo where it does not.
    public TremoloState Audible()
    {
        return IsActive ? this : Off;
    }

    // This state with its delay byte replaced and its other values untouched.
    public TremoloState WithDelay(int delay)
    {
        return this with { Delay = delay };
    }
}
