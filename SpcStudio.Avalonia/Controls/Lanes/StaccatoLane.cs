using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The lane holding the song's light staccato, $F4 $02, as units: each a stretch of the song played under staccato.
public sealed class StaccatoLane : SongUnitLane<StaccatoUnit>
{
    // Draws the lane in its own two colors.
    public StaccatoLane() : base(LaneCellBrushes.Of(Palette.StaccatoCell, Palette.StaccatoCellSelected))
    {
    }

    // The song's staccato units.
    protected override IReadOnlyList<StaccatoUnit> Units
    {
        get
        {
            return Song?.StaccatoUnits ?? [];
        }
    }

    // The units as a change stream: on where each stretch they cover starts and off where it ends.
    protected override IEnumerable<LaneValueChange> Changes
    {
        get
        {
            return StaccatoRuns.StateChanges(Units);
        }
    }

    public override ValueRange ValueRange
    {
        get
        {
            return SnesTiming.StaccatoRange;
        }
    }

    // A fresh unit over a stretch.
    protected override StaccatoUnit NewUnit(int startTick, int lengthTicks)
    {
        return new StaccatoUnit(startTick, lengthTicks);
    }
}
