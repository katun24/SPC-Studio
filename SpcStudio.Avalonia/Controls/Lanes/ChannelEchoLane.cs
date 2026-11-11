using Avalonia;
using Avalonia.Media;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// The lane holding whether one channel is fed into the echo buffer, its bit in $EF's mask, as units: each one a stretch the channel is wet over.
public sealed class ChannelEchoLane : UnitLane<EchoSendUnit>
{
    private const int DryValue = 0;
    private const int WetValue = 1;

    private static readonly ValueRange SendRange = new(DryValue, WetValue);
    private static readonly Pen OverriddenCellBorder = NoteBlock.BorderPen(OverriddenRegionBrush);
    private static readonly Pen OverriddenDryStripePen = NoteBlock.CutStripePenOf(OverriddenRegionBrush);

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

    // Draws a stretch the notes keep dry as stripes in the override color, and one they keep wet as a cell in it translucent as an overridden region
    // is, either one a solid cell in it where it covers one of the lane's own units.
    protected override void DrawOverriddenRegion(DrawingContext dc, DrawnRegion region, double x0, double x1)
    {
        var box = BoxSpanning(x0, x1 - x0);
        if (region.Value == WetValue)
        {
            using (dc.PushOpacity(RegionLook.BandOpacity))
            {
                DrawOverriddenCell(dc, box);
            }
        }
        else
        {
            NoteBlock.DrawCutStripes(dc, new RectangleGeometry(box), box, OverriddenDryStripePen);
        }

        foreach (var part in PartsWithin(DrawnUnitSpans, [region]))
        {
            using (ClipToTicks(dc, part.StartTick, part.EndTick))
            {
                DrawOverriddenCell(dc, box);
            }
        }
    }

    // Draws a cell in the override color with the border a cell of that color wears.
    private static void DrawOverriddenCell(DrawingContext dc, Rect box)
    {
        dc.DrawRectangle(OverriddenRegionBrush, OverriddenCellBorder, box);
    }
}
