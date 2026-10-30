using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The lane holding one channel's amplify, $FA $03, drawn as regions that never fade.
public sealed class AmplifyLane : NoteMergedLane
{
    // Draws the lane in its own two colors.
    public AmplifyLane() : base(LaneCellBrushes.Of(Palette.AmplifyCell, Palette.AmplifyCellSelected))
    {
    }

    // The channel's own amplify dividers.
    protected override IEnumerable<LaneValueChange> Changes
    {
        get
        {
            return Song is null ? [] : Song.AmplifyChanges(Channel);
        }
    }

    // The stretches the notes' own amplify decides.
    protected override IEnumerable<DrawnRegion> OverriddenRegions
    {
        get
        {
            return FlatOverriddenRegions(NoteAmplify.Lane);
        }
    }

    // The channel's base amplify.
    public override int BaseValue
    {
        get
        {
            return Song?.BaseAmplify(Channel) ?? DefaultValue;
        }
    }

    public override int DefaultValue
    {
        get
        {
            return SnesVolume.DefaultAmplify;
        }
    }

    public override ValueRange ValueRange
    {
        get
        {
            return SnesVolume.AmplifyRange;
        }
    }

    // The row as the channel plays it, its dividers merged with its notes' own amplify.
    protected override IEnumerable<DrawnRegion> MergedRegionsDrawn(int endTick)
    {
        var (mergedBase, mergedDividers) = NoteAmplify.Lane.Merged(Song!, Channel, PreviewedNotes());
        return RegionsBetweenDividers(mergedBase, mergedDividers, NoFades, endTick);
    }

    // The amplify a change at a tick sets, or null where none sits there.
    public override int? ChangeValueAt(int tick)
    {
        return Song?.AmplifyChangeAt(Channel, tick)?.Value;
    }
}
