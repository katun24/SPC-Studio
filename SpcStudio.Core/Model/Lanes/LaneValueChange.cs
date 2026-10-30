namespace SpcStudio.Model;

// A lane's value changing at a tick, any fade held apart: what a lane whose command only ever sets its value stores, and what every lane draws its
// dividers as.
public readonly record struct LaneValueChange(int Tick, int Value) : ILaneChange
{
    // A divider of a lane drawn as regions as the value it sets at its tick.
    public static LaneValueChange Of(LaneDivider<int> divider)
    {
        return new(divider.Tick, divider.Value);
    }
}
