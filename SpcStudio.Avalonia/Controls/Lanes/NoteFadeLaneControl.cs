using SpcStudio.Model;

namespace SpcStudio.Controls;

// A fading region lane whose notes can lay a chain of their own over it - the channel volume's and the pan's: the row is drawn as the lane and the
// notes merged, with the stretches the notes decide drawn in the override color in place of the lane's.
public abstract class NoteFadeLaneControl : ChannelChangeLane
{
    // Takes the two brushes the lane's cells are drawn in.
    protected NoteFadeLaneControl(LaneCellBrushes cellBrushes) : base(cellBrushes)
    {
    }

    public override bool CanFade
    {
        get
        {
            return true;
        }
    }

    // The stretches the notes' own chains decide, at the values the channel plays across them.
    protected override IEnumerable<DrawnRegion> OverriddenRegions
    {
        get
        {
            if (Song is null)
            {
                return [];
            }

            var previewedNotes = PreviewedNotes();
            var regions = RegionsDrawnUpTo(MergedRegions(previewedNotes), GridEndTick).ToList();
            return PartsWithin(OverrideSpans(previewedNotes), regions);
        }
    }

    // The channel's regions with the given notes laid over the lane, or the song's own notes where none are given.
    protected abstract IReadOnlyList<LaneRegion> MergedRegions(IReadOnlyList<Note>? notes);

    // The stretches the given notes decide rather than the lane, or the song's own notes where none are given.
    protected abstract IReadOnlyList<(int Start, int End)> OverrideSpans(IReadOnlyList<Note>? notes);

    // The run of regions the row is drawn as up to the end tick, the last one running out to it: the lane and its notes merged, or the lane's own shape
    // while a divider is dragged.
    protected override IEnumerable<DrawnRegion> RegionsDrawn(int endTick)
    {
        if (Song is null || IsDragging)
        {
            return base.RegionsDrawn(endTick);
        }

        return RegionsDrawnUpTo(MergedRegions(PreviewedNotes()), endTick);
    }

    // Regions up to an end tick as they are drawn, the last one running out to it.
    private static IEnumerable<DrawnRegion> RegionsDrawnUpTo(IReadOnlyList<LaneRegion> regions, int endTick)
    {
        for (int i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            if (region.Start >= endTick)
            {
                yield break;
            }

            int end = i + 1 < regions.Count ? region.End : Math.Max(region.End, endTick);
            yield return new DrawnRegion(region.Start, end, region.Value, region.Fades, region.FadeTo);
        }
    }
}
