using SpcStudio.Model;

namespace SpcStudio.Controls;

// A lane of units whose units are the song's rather than one channel's.
public abstract class SongUnitLane<TUnit> : UnitLane<TUnit>
    where TUnit : struct, ILaneUnit<TUnit>
{
    // Takes the two brushes the units are drawn in.
    protected SongUnitLane(LaneCellBrushes cellBrushes) : base(cellBrushes)
    {
    }

    public override bool IsPerChannel
    {
        get
        {
            return false;
        }
    }
}
