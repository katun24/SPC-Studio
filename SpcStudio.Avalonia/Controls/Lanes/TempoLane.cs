using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The lane holding the song's tempo, MML t, with its $E3 ramps, drawn as regions.
public sealed class TempoLane : ChangeLane
{
    // Draws the lane in its own two colors.
    public TempoLane() : base(LaneCellBrushes.Of(Palette.TempoCell, Palette.TempoCellSelected))
    {
    }

    // The song's tempo dividers.
    protected override IEnumerable<LaneValueChange> Changes
    {
        get
        {
            return Song is null ? [] : Song.TempoChanges.Select(LaneValueChange.Of);
        }
    }

    public override bool CanFade
    {
        get
        {
            return true;
        }
    }

    // The song's base tempo, which the first region holds.
    public override int BaseValue
    {
        get
        {
            return Song?.BaseTempo ?? DefaultValue;
        }
    }

    public override int DefaultValue
    {
        get
        {
            return SnesTiming.DefaultTempo;
        }
    }

    public override ValueRange ValueRange
    {
        get
        {
            return SnesTiming.TempoRange;
        }
    }

    // The song's tempo regions.
    protected override IEnumerable<LaneRegion> OwnRegions(Song song)
    {
        return LaneRegions.Tempo(song);
    }

    // The tempo a divider at a tick sets, or null where none sits there.
    public override int? ChangeValueAt(int tick)
    {
        return Song?.TempoChangeAt(tick)?.Value;
    }
}
