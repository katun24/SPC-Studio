using SpcStudio.Model;

namespace SpcStudio.Controls;

// A fading region lane whose notes can lay a chain of their own over it - the channel volume's and the pan's: the row is drawn as the lane and the
// notes merged, with the stretches the notes decide drawn over it in the override color.
public abstract class NoteFadeLaneControl : NoteMergedLane
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
            var regions = MergedRegions(previewedNotes);
            return OverrideSpans(previewedNotes).SelectMany(span => RegionsClippedTo(regions, span.Start, span.End));
        }
    }

    // The channel's regions with the given notes laid over the lane, or the song's own notes where none are given.
    protected abstract IReadOnlyList<LaneRegion> MergedRegions(IReadOnlyList<Note>? notes);

    // The stretches the given notes decide rather than the lane, or the song's own notes where none are given.
    protected abstract IReadOnlyList<(int Start, int End)> OverrideSpans(IReadOnlyList<Note>? notes);

    // The merged regions up to the end tick, the last one running out to it.
    protected override IEnumerable<DrawnRegion> MergedRegionsDrawn(int endTick)
    {
        var regions = MergedRegions(PreviewedNotes());
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

    // The parts of the regions inside one span, a ramp clipped by it keeping its own slope.
    private static IEnumerable<DrawnRegion> RegionsClippedTo(IReadOnlyList<LaneRegion> regions, int spanStart, int spanEnd)
    {
        foreach (var region in regions)
        {
            int clippedStart = Math.Max(region.Start, spanStart);
            int clippedEnd = Math.Min(region.End, spanEnd);
            if (clippedEnd <= clippedStart)
            {
                continue;
            }

            yield return new DrawnRegion(clippedStart, clippedEnd, ValueReachedAt(region, clippedStart), region.Fades, ValueReachedAt(region, clippedEnd));
        }
    }

    // Where a region has got to at a tick, a ramp included.
    private static int ValueReachedAt(LaneRegion region, int tick)
    {
        if (!region.Fades || region.LengthTicks <= 0 || tick <= region.Start)
        {
            return region.Value;
        }

        return region.Value + (int)Math.Round((double)(region.FadeTo - region.Value) * (tick - region.Start) / region.LengthTicks);
    }
}
