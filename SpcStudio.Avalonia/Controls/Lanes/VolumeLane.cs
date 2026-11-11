using SpcStudio.Model;

namespace SpcStudio.Controls;

// The lane holding the song's song volume, MML w, with its $E1 fades, drawn as regions.
public sealed class VolumeLane : ChangeLane
{
    // Draws the lane in the volume lanes' two colors.
    public VolumeLane() : base(LaneCellBrushes.Volume)
    {
    }

    // The song's song volume dividers.
    protected override IEnumerable<LaneValueChange> Changes
    {
        get
        {
            return Song is null ? [] : Song.VolumeChanges.Select(LaneValueChange.Of);
        }
    }

    public override bool CanFade
    {
        get
        {
            return true;
        }
    }

    // The song's base song volume, which the first region holds.
    public override int BaseValue
    {
        get
        {
            return Song?.BaseMasterVolume ?? DefaultValue;
        }
    }

    public override int DefaultValue
    {
        get
        {
            return SnesVolume.DefaultMasterVolume;
        }
    }

    public override ValueRange ValueRange
    {
        get
        {
            return SnesVolume.MasterVolumeRange;
        }
    }

    // The song's song volume regions.
    protected override IEnumerable<LaneRegion> OwnRegions(Song song)
    {
        return LaneRegions.MasterVolume(song);
    }

    // The song volume a divider at a tick sets, or null where none sits there.
    public override int? ChangeValueAt(int tick)
    {
        return Song?.VolumeChangeAt(tick)?.Value;
    }
}
