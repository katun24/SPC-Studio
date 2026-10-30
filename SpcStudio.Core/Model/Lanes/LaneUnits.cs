namespace SpcStudio.Model;

// What every lane of units does: tell where a unit ends and whether it covers a tick or overlaps another, put one down with the overlap cut out of
// whatever was there, find the one covering a tick, and edit a batch.
public static class LaneUnits
{
    // The first tick past a unit, which it does not itself cover.
    public static int EndTick<TUnit>(this TUnit unit) where TUnit : struct, ILaneUnit<TUnit>
    {
        return unit.StartTick + unit.LengthTicks;
    }

    // Whether a unit covers a tick.
    public static bool Contains<TUnit>(this TUnit unit, double tick) where TUnit : struct, ILaneUnit<TUnit>
    {
        return tick >= unit.StartTick && tick < unit.EndTick();
    }

    // Whether two units share any tick.
    public static bool Overlaps<TUnit>(this TUnit unit, TUnit other) where TUnit : struct, ILaneUnit<TUnit>
    {
        return unit.StartTick < other.EndTick() && other.StartTick < unit.EndTick();
    }

    // Puts a unit down, cutting it out of any it overlaps.
    public static void Place<TUnit>(List<TUnit> units, TUnit unit) where TUnit : struct, ILaneUnit<TUnit>
    {
        // The unit with its start and length held to what a lane can hold.
        var spannedUnit = unit.WithSpan(Math.Max(0, unit.StartTick), Math.Max(1, unit.LengthTicks));
        InsertCuttingOverlaps(units, spannedUnit, piece => piece);
    }

    // Inserts a unit as it is, in start order, cutting it out of any it overlaps and handing each piece left over through a last step.
    public static void InsertCuttingOverlaps<TUnit>(List<TUnit> units, TUnit unit, Func<TUnit, TUnit> finishPiece) where TUnit : struct, ILaneUnit<TUnit>
    {
        int placedStart = unit.StartTick;
        int placedEnd = unit.EndTick();
        for (int unitIndex = units.Count - 1; unitIndex >= 0; unitIndex--)
        {
            var existing = units[unitIndex];
            if (existing.LengthTicks > 0 && existing.EndTick() <= placedStart)
            {
                break;
            }

            if (!existing.Overlaps(unit))
            {
                continue;
            }

            int existingStart = existing.StartTick;
            int existingEnd = existing.EndTick();
            units.RemoveAt(unitIndex);
            if (existingEnd > placedEnd)
            {
                units.Insert(unitIndex, finishPiece(existing.WithSpan(placedEnd, existingEnd - placedEnd)));
            }

            if (existingStart < placedStart)
            {
                units.Insert(unitIndex, finishPiece(existing.WithSpan(existingStart, placedStart - existingStart)));
            }
        }

        units.Insert(LastIndexStartingBy(units, placedStart) + 1, unit);
    }

    // Where the last unit starting at or before a tick stands in a lane, or -1 where none does.
    public static int LastIndexStartingBy<TUnit>(IReadOnlyList<TUnit> units, int tick) where TUnit : struct, ILaneUnit<TUnit>
    {
        return SortedSearch.CountLeading(units, unit => unit.StartTick <= tick) - 1;
    }

    // Where the unit starting at a tick stands in a lane, or -1 where none does.
    public static int IndexStartingAt<TUnit>(IReadOnlyList<TUnit> units, int tick) where TUnit : struct, ILaneUnit<TUnit>
    {
        int unitIndex = LastIndexStartingBy(units, tick);
        return unitIndex >= 0 && units[unitIndex].StartTick == tick ? unitIndex : -1;
    }

    // Where the unit covering a tick stands in a lane, or -1 where none does.
    public static int IndexCovering<TUnit>(IReadOnlyList<TUnit> units, int tick) where TUnit : struct, ILaneUnit<TUnit>
    {
        int unitIndex = LastIndexStartingBy(units, tick);
        while (unitIndex >= 0 && units[unitIndex].LengthTicks == 0)
        {
            unitIndex--;
        }

        return unitIndex >= 0 && units[unitIndex].Contains(tick) ? unitIndex : -1;
    }

    // The unit covering a tick, or null where none does.
    public static TUnit? UnitCovering<TUnit>(IReadOnlyList<TUnit> units, int tick) where TUnit : struct, ILaneUnit<TUnit>
    {
        return UnitAtIndex(units, IndexCovering(units, tick));
    }

    // The unit starting at a tick, or null where none does.
    public static TUnit? UnitStartingAt<TUnit>(IReadOnlyList<TUnit> units, int tick) where TUnit : struct, ILaneUnit<TUnit>
    {
        return UnitAtIndex(units, IndexStartingAt(units, tick));
    }

    // The unit at an index a search answered, or null where it answered -1.
    private static TUnit? UnitAtIndex<TUnit>(IReadOnlyList<TUnit> units, int unitIndex) where TUnit : struct, ILaneUnit<TUnit>
    {
        return unitIndex >= 0 ? units[unitIndex] : null;
    }

    // Takes away the units starting at the given ticks and puts the given ones down, answering whether anything was asked of the list.
    public static bool RemoveAndPlace<TUnit>(List<TUnit> units, IReadOnlyList<int> removedStartTicks, IReadOnlyList<TUnit> placedUnits) where TUnit : struct, ILaneUnit<TUnit>
    {
        int removedUnitCount = removedStartTicks.Count == 0 ? 0 : units.RemoveAll(unit => removedStartTicks.Contains(unit.StartTick));
        foreach (var unit in placedUnits)
        {
            Place(units, unit);
        }

        return removedUnitCount > 0 || placedUnits.Count > 0;
    }
}
