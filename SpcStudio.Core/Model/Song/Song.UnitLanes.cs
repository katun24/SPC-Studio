namespace SpcStudio.Model;

// The song's lane of stretches with no base beneath it: the light staccato.
public sealed partial class Song
{
    private readonly List<StaccatoUnit> _staccatoUnits = [];

    // The light staccato units, in start order and never overlapping.
    public IReadOnlyList<StaccatoUnit> StaccatoUnits
    {
        get
        {
            return _staccatoUnits;
        }
    }

    // Puts one light staccato unit down, cutting it out of any it overlaps.
    public void PlaceStaccato(StaccatoUnit unit)
    {
        EditStaccato([], [unit]);
    }

    // Puts a group of light staccato units down at once, following the roll once around the group.
    public void PlaceAllStaccato(IReadOnlyList<StaccatoUnit> units)
    {
        EditStaccato([], units);
    }

    // Takes away the light staccato units starting at the given ticks and puts the given ones down, and answers whether anything was asked.
    public bool EditStaccato(IReadOnlyList<int> removedStartTicks, IReadOnlyList<StaccatoUnit> placedUnits)
    {
        return EditUnits(_staccatoUnits, removedStartTicks, placedUnits);
    }

    // Replaces the light staccato units as given.
    public void SetStaccatoUnits(IReadOnlyList<StaccatoUnit> units)
    {
        ReplaceContents(_staccatoUnits, units);
    }

    // Takes away a unit lane's units starting at the given ticks and puts the given ones down, following the roll once around the pair where anything was asked, and answers whether it was.
    private bool EditUnits<TUnit>(List<TUnit> units, IReadOnlyList<int> removedStartTicks, IReadOnlyList<TUnit> placedUnits) where TUnit : struct, ILaneUnit<TUnit>
    {
        int previousRollTicks = RollLengthTicks;
        if (!LaneUnits.RemoveAndPlace(units, removedStartTicks, placedUnits))
        {
            return false;
        }

        FollowRoll(previousRollTicks);
        return true;
    }
}
