namespace SpcStudio.Model;

// One channel's $FC arms and clears as derived from its notes: whether its header arms the slot, the commands after it, and the slot they leave.
public sealed record HoldCommandList(bool DoesHeaderArm, IReadOnlyList<(int Tick, bool Arms)> ArmAndClearTicks)
{
    // The arms and clears of a key-on slot armed over stretches: armed where each starts, armed again behind every clear of both slots inside it, and
    // cleared where it ends.
    public static HoldCommandList ForStretches(IEnumerable<(int ArmTick, int ClearTick)> stretches, IReadOnlyCollection<int> clearTicksOfBothSlots)
    {
        var armAndClearTicks = new List<(int Tick, bool Arms)>();

        foreach (var (armTick, clearTick) in stretches)
        {
            armAndClearTicks.Add((armTick, true));
            armAndClearTicks.AddRange(ClearTicksInside(clearTicksOfBothSlots, armTick, clearTick).Select(tick => (tick, true)));
            armAndClearTicks.Add((clearTick, false));
        }

        return new HoldCommandList(false, armAndClearTicks);
    }

    // The clears of both slots strictly inside a stretch, each once and in order, behind each of which the stretch's slot is armed again.
    public static IEnumerable<int> ClearTicksInside(IReadOnlyCollection<int> clearTicksOfBothSlots, int armTick, int clearTick)
    {
        return clearTicksOfBothSlots.Where(tick => tick > armTick && tick < clearTick).Distinct().Order();
    }

    // Whether the slot is armed at a tick, the commands at that tick included.
    public bool IsArmedAt(int tick)
    {
        int commandCountUpToTick = SortedSearch.CountLeading(ArmAndClearTicks, command => command.Tick <= tick);
        return commandCountUpToTick == 0 ? DoesHeaderArm : ArmAndClearTicks[commandCountUpToTick - 1].Arms;
    }
}
