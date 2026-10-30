namespace SpcStudio.Model;

// One channel's $FC arms and clears as derived from its notes: whether its header arms the slot, the commands after it, and the slot they leave.
public sealed record HoldCommandList(bool DoesHeaderArm, IReadOnlyList<(int Tick, bool Arms)> ArmAndClearTicks)
{
    // Whether the slot is armed at a tick, the commands at that tick included.
    public bool IsArmedAt(int tick)
    {
        int commandCountUpToTick = SortedSearch.CountLeading(ArmAndClearTicks, command => command.Tick <= tick);
        return commandCountUpToTick == 0 ? DoesHeaderArm : ArmAndClearTicks[commandCountUpToTick - 1].Arms;
    }
}
