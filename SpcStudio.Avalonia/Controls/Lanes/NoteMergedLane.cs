namespace SpcStudio.Controls;

// A lane of one channel whose notes can decide its value over stretches of it - the amplify's, the channel volume's and the pan's: the row is drawn as
// the lane and the notes merged, or as the lane's own shape while a divider is dragged.
public abstract class NoteMergedLane : ChannelChangeLane
{
    // Takes the two brushes the lane's cells are drawn in.
    protected NoteMergedLane(LaneCellBrushes cellBrushes) : base(cellBrushes)
    {
    }

    // The run of regions the row is drawn as: the lane and its notes merged, or the lane's own shape while a divider is dragged.
    protected override IEnumerable<DrawnRegion> RegionsDrawn(int endTick)
    {
        if (Song is null || IsDragging)
        {
            return base.RegionsDrawn(endTick);
        }

        return MergedRegionsDrawn(endTick);
    }

    // The regions the channel plays, its notes laid over the lane, up to the end tick.
    protected abstract IEnumerable<DrawnRegion> MergedRegionsDrawn(int endTick);
}
