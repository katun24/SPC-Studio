namespace SpcStudio.Model;

// A lane's value changing at a tick, any fade held apart: what a lane whose command only ever sets its value stores, and what every lane draws its
// dividers as.
public readonly record struct LaneValueChange(int Tick, int Value) : ILaneChange<LaneValueChange>
{
    // The same change at another tick.
    public LaneValueChange WithTick(int tick)
    {
        return this with { Tick = tick };
    }

    // A divider of a lane drawn as regions as the value it sets at its tick.
    public static LaneValueChange Of(LaneDivider<int> divider)
    {
        return new(divider.Tick, divider.Value);
    }
}
