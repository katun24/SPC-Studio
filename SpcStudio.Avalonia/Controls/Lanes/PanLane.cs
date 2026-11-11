using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The lane holding one channel's pan, MML y, with its $DC fades, drawn as regions with the notes' own pans over them.
public sealed class PanLane : NoteFadeLaneControl
{
    // Draws the lane in its own two colors.
    public PanLane() : base(LaneCellBrushes.Of(Palette.PanCell, Palette.PanCellSelected))
    {
    }

    // The channel's own pan dividers.
    protected override IEnumerable<LaneValueChange> Changes
    {
        get
        {
            return Song is null ? [] : Song.PanChanges(Channel).Select(change => new LaneValueChange(change.Tick, change.Value));
        }
    }

    // The channel's base pan.
    public override int BaseValue
    {
        get
        {
            return Song?.BasePan(Channel) ?? DefaultValue;
        }
    }

    public override int DefaultValue
    {
        get
        {
            return SnesVolume.DefaultPanIndex;
        }
    }

    public override ValueRange ValueRange
    {
        get
        {
            return SnesVolume.PanIndexRange;
        }
    }

    // The channel's own pan regions, its notes' own pans left out.
    protected override IEnumerable<LaneRegion> OwnRegions(Song song)
    {
        return LaneRegions.PanOwn(song, Channel).Select(pan => pan.Region);
    }

    // The regions the channel plays, its dividers merged with the given notes' own pans.
    protected override IReadOnlyList<LaneRegion> MergedRegions(IReadOnlyList<Note>? notes)
    {
        return Song is null ? [] : [.. LaneRegions.Pan(Song, Channel, notes).Select(pan => pan.Region)];
    }

    // The stretches the given notes' own pans decide.
    protected override IReadOnlyList<(int Start, int End)> OverrideSpans(IReadOnlyList<Note>? notes)
    {
        return Song is null ? [] : NotePan.Lane.OverriddenSpans(Song, Channel, notes);
    }

    // The pan a change at a tick sets, or null where none sits there.
    public override int? ChangeValueAt(int tick)
    {
        return Song?.PanChangeAt(Channel, tick)?.Value;
    }
}
