using SpcStudio.Model;

namespace SpcStudio.Controls;

// The lane holding whether one channel is fed into the echo buffer, its bit in $EF's mask, as units: each one a stretch the channel is wet over.
public sealed class ChannelEchoLane : UnitLane<EchoSendUnit>
{
    private const int DryValue = 0;
    private const int WetValue = 1;

    private static readonly ValueRange SendRange = new(DryValue, WetValue);

    // Draws the lane in the echo lanes' two colors.
    public ChannelEchoLane() : base(LaneCellBrushes.Echo)
    {
    }

    // The stretches the channel is wet over.
    protected override IReadOnlyList<EchoSendUnit> Units
    {
        get
        {
            return Song?.ChannelEchoUnits(Channel) ?? [];
        }
    }

    // The units as a change stream, wet at each one's start and dry at its end.
    protected override IEnumerable<LaneValueChange> Changes
    {
        get
        {
            return Units.SelectMany(unit => new[] { new LaneValueChange(unit.StartTick, WetValue), new LaneValueChange(unit.EndTick(), DryValue) });
        }
    }

    // The stretches the notes' own echo send decides.
    protected override IEnumerable<DrawnRegion> OverriddenRegions
    {
        get
        {
            return FlatOverriddenRegions(NoteEchoSend.Lane);
        }
    }

    public override ValueRange ValueRange
    {
        get
        {
            return SendRange;
        }
    }

    // A fresh wet stretch.
    protected override EchoSendUnit NewUnit(int startTick, int lengthTicks)
    {
        return new EchoSendUnit(startTick, lengthTicks);
    }
}
