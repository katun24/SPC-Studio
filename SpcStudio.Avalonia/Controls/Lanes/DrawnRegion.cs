namespace SpcStudio.Controls;

// One stretch of a lane as it is drawn: the ticks it runs between, the value it opens at, and whether and to what it ramps.
public readonly record struct DrawnRegion(int StartTick, int EndTick, int Value, bool DoesFade, int FadeTo)
{
    // The part of the stretch between two ticks, a ramp keeping its own slope, or null where the two do not overlap.
    public DrawnRegion? ClippedTo(int fromTick, int toTick)
    {
        int clippedStart = Math.Max(StartTick, fromTick);
        int clippedEnd = Math.Min(EndTick, toTick);
        if (clippedEnd <= clippedStart)
        {
            return null;
        }

        return new DrawnRegion(clippedStart, clippedEnd, ValueReachedAt(clippedStart), DoesFade, ValueReachedAt(clippedEnd));
    }

    // Where the stretch has got to at a tick, a ramp included.
    private int ValueReachedAt(int tick)
    {
        if (!DoesFade || EndTick <= StartTick || tick <= StartTick)
        {
            return Value;
        }

        return Value + (int)Math.Round((double)(FadeTo - Value) * (tick - StartTick) / (EndTick - StartTick));
    }
}
