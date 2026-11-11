namespace SpcStudio.Model;

// A song's light staccato units read as the driver sees them: the runs they cover and the state in force at a tick.
public static class StaccatoRuns
{
    // The stretches the units cover, in tick order, units that touch merged into one run.
    public static IEnumerable<(int StartTick, int EndTick)> CoveredRuns(IReadOnlyList<StaccatoUnit> units)
    {
        int runStartTick = -1;
        int runEndTick = -1;
        foreach (var unit in units)
        {
            if (runStartTick < 0)
            {
                (runStartTick, runEndTick) = (unit.StartTick, unit.EndTick());
                continue;
            }

            if (unit.StartTick <= runEndTick)
            {
                runEndTick = Math.Max(runEndTick, unit.EndTick());
                continue;
            }

            yield return (runStartTick, runEndTick);
            (runStartTick, runEndTick) = (unit.StartTick, unit.EndTick());
        }

        if (runStartTick >= 0)
        {
            yield return (runStartTick, runEndTick);
        }
    }

    // The state at a tick as the light staccato lane holds it: on where a unit covers the tick and off where none does.
    public static int StateAt(IReadOnlyList<StaccatoUnit> units, int tick)
    {
        return LaneUnits.UnitCovering(units, tick) is null ? SnesTiming.MinStaccato : SnesTiming.MaxStaccato;
    }

    // Every tick the state changes at, with the state it changes to, in tick order: the edges of the covered runs.
    public static IEnumerable<LaneValueChange> StateChanges(IReadOnlyList<StaccatoUnit> units)
    {
        foreach (var (startTick, endTick) in CoveredRuns(units))
        {
            yield return new(startTick, SnesTiming.MaxStaccato);
            yield return new(endTick, SnesTiming.MinStaccato);
        }
    }
}
