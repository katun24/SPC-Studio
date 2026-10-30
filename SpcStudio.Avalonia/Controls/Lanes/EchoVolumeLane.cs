using SpcStudio.Model;

namespace SpcStudio.Controls;

// The lane holding the song's echo volume, both sides of it in one row of regions, with the $F2 fades laid over them.
public sealed class EchoVolumeLane : ChangeLane
{
    // Draws the lane in the echo lanes' two colors.
    public EchoVolumeLane() : base(LaneCellBrushes.Echo)
    {
    }

    // The left side's dividers, which stand on the same ticks as the right side's.
    protected override IEnumerable<LaneValueChange> Changes
    {
        get
        {
            return Song is null ? [] : Song.EchoChanges(EchoSetting.VolumeLeft).Select(LaneValueChange.Of);
        }
    }

    public override bool CanFade
    {
        get
        {
            return true;
        }
    }

    // The left side's base volume.
    public override int BaseValue
    {
        get
        {
            return Song?.BaseEchoVolume(EchoSetting.VolumeLeft) ?? DefaultValue;
        }
    }

    // The default of either side, the two sides sharing one.
    public override int DefaultValue
    {
        get
        {
            return SnesEcho.DefaultVolume;
        }
    }

    // Whether either side carries a divider.
    public override bool HasChanges
    {
        get
        {
            return Song is not null && (Song.EchoChanges(EchoSetting.VolumeLeft).Count > 0 || Song.EchoChanges(EchoSetting.VolumeRight).Count > 0);
        }
    }

    public override ValueRange ValueRange
    {
        get
        {
            return SnesEcho.VolumeRange;
        }
    }

    // The left side's regions, the right side's standing on the same ticks.
    protected override IEnumerable<LaneRegion> OwnRegions(Song song)
    {
        return LaneRegions.EchoVolume(song).Select(region => region.Left);
    }

    // The right side's value and fade target across the region starting at a tick, or null where no region starts there.
    protected override (int Value, int? To)? SecondValueAt(int tick)
    {
        if (Song is null)
        {
            return null;
        }

        foreach (var (_, right) in LaneRegions.EchoVolume(Song))
        {
            if (right.Start == tick)
            {
                return (right.Value, right.Fades ? right.FadeTo : null);
            }
        }

        return null;
    }

    // The left side's divider value at a tick, or null where none stands there.
    public override int? ChangeValueAt(int tick)
    {
        return Song?.EchoChangeAt(EchoSetting.VolumeLeft, tick)?.Value;
    }
}
