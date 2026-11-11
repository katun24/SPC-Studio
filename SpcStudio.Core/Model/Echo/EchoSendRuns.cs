namespace SpcStudio.Model;

// One channel's echo bit read both ways: the driver's base and dividers as the units the lane draws, and those units back as the base and dividers.
public static class EchoSendRuns
{
    // The stretches a channel is fed into the buffer over, in tick order, a run still on where the song ends reaching the song's end.
    public static IReadOnlyList<EchoSendUnit> UnitsFromDividers(bool isBaseOn, IReadOnlyList<LaneValueChange> changes, int totalTicks)
    {
        var units = new List<EchoSendUnit>();
        bool isOn = isBaseOn;
        int onSinceTick = 0;
        foreach (var change in changes.OrderBy(change => change.Tick))
        {
            bool isOnAfterChange = change.Value != 0;
            if (isOnAfterChange == isOn)
            {
                continue;
            }

            if (isOn && change.Tick > onSinceTick)
            {
                units.Add(new EchoSendUnit(onSinceTick, change.Tick - onSinceTick));
            }

            isOn = isOnAfterChange;
            onSinceTick = change.Tick;
        }

        if (isOn && totalTicks > onSinceTick)
        {
            units.Add(new EchoSendUnit(onSinceTick, totalTicks - onSinceTick));
        }

        return units;
    }

    // The units read back as whether the base bit is set and the dividers over it, units that touch being one run.
    public static (bool IsBaseOn, IReadOnlyList<LaneValueChange> Changes) DividersFromUnits(IReadOnlyList<EchoSendUnit> units, int totalTicks)
    {
        var runs = new List<(int StartTick, int EndTick)>();
        foreach (var unit in units.OrderBy(unit => unit.StartTick))
        {
            if (runs.Count > 0 && unit.StartTick <= runs[^1].EndTick)
            {
                runs[^1] = (runs[^1].StartTick, Math.Max(runs[^1].EndTick, unit.EndTick()));
            }
            else
            {
                runs.Add((unit.StartTick, unit.EndTick()));
            }
        }

        var changes = new List<LaneValueChange>();

        // Whether a run covers the song's first tick.
        bool isBaseOn = runs.Count > 0 && runs[0].StartTick <= 0;
        foreach (var (startTick, endTick) in runs)
        {
            if (startTick > 0)
            {
                changes.Add(new LaneValueChange(startTick, 1));
            }

            if (endTick < totalTicks)
            {
                changes.Add(new LaneValueChange(endTick, 0));
            }
        }

        return (isBaseOn, changes);
    }
}
