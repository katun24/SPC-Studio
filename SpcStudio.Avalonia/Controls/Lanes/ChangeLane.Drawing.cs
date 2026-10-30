using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The lane's drawing: the background and the grid, and the regions, their dividers, their values and the stretches a note decides.
public abstract partial class ChangeLane
{
    private const double GridLineThickness = 1;
    private const double RegionLineThickness = 1.4;
    private const double SecondLineThickness = 0.7;
    private const double BandOpacity = 0.22;
    private const double RegionLineOpacity = 0.75;
    private const double ValueFontSize = 10;
    private const string FadeArrow = " > ";
    private const string SecondValueSeparator = "/";

    private static readonly Brush OverriddenRegionBrush = new SolidColorBrush(Palette.LaneOverridden);
    private readonly Pen _stepLine = new(new SolidColorBrush(Palette.CharcoalGray), GridLineThickness);
    private readonly Pen _barLine = new(new SolidColorBrush(Palette.SteelGray), GridLineThickness);
    private Pen? _overriddenPen;
    private IBrush? _bandBrush;
    private Pen? _regionLinePen;
    private IBrush? _selectedBandBrush;
    private Pen? _selectedRegionLinePen;
    private Pen? _secondLinePen;
    private Pen? _selectedSecondLinePen;
    private Pen? _dividerPen;
    private Pen? _selectedDividerPen;
    private Pen? _cellBorder;
    private Pen? _selectedCellBorder;

    // The fades of a lane whose regions never ramp.
    protected static readonly IReadOnlyDictionary<int, (int Ticks, int To)> NoFades = new Dictionary<int, (int Ticks, int To)>();

    // The stretches of this lane a note decides rather than the lane, each at the value it plays, empty on a lane no note overrides.
    protected virtual IEnumerable<DrawnRegion> OverriddenRegions
    {
        get
        {
            return [];
        }
    }

    // The line a stretch a note decides is drawn with.
    private Pen OverriddenPen
    {
        get
        {
            return _overriddenPen ??= new Pen(OverriddenRegionBrush, RegionLineThickness);
        }
    }

    // The fill under a region's line and under a fade's slope.
    private IBrush BandBrush
    {
        get
        {
            return _bandBrush ??= WithOpacity(CellBrushes.Cell, BandOpacity);
        }
    }

    // A region's line and a fade's slope.
    private Pen RegionLinePen
    {
        get
        {
            return _regionLinePen ??= new Pen(WithOpacity(CellBrushes.Cell, RegionLineOpacity), RegionLineThickness);
        }
    }

    // The fill under the selected region's line.
    private IBrush SelectedBandBrush
    {
        get
        {
            return _selectedBandBrush ??= WithOpacity(CellBrushes.Selected, BandOpacity);
        }
    }

    // The selected region's line.
    private Pen SelectedRegionLinePen
    {
        get
        {
            return _selectedRegionLinePen ??= new Pen(WithOpacity(CellBrushes.Selected, RegionLineOpacity), RegionLineThickness);
        }
    }

    // The line a region's second value is drawn with, the first line's color at a lighter weight.
    private Pen SecondLinePen
    {
        get
        {
            return _secondLinePen ??= new Pen(WithOpacity(CellBrushes.Cell, RegionLineOpacity), SecondLineThickness);
        }
    }

    // The selected region's second-value line.
    private Pen SelectedSecondLinePen
    {
        get
        {
            return _selectedSecondLinePen ??= new Pen(WithOpacity(CellBrushes.Selected, RegionLineOpacity), SecondLineThickness);
        }
    }

    // The line between two regions.
    private Pen DividerPen
    {
        get
        {
            return _dividerPen ??= new Pen(CellBrushes.Cell, GridLineThickness);
        }
    }

    // The line opening the selected region.
    private Pen SelectedDividerPen
    {
        get
        {
            return _selectedDividerPen ??= new Pen(CellBrushes.Selected, GridLineThickness);
        }
    }

    // The border a unit wears, derived from its fill the way a note's is.
    protected Pen CellBorder
    {
        get
        {
            return _cellBorder ??= NoteBlock.BorderPen(CellBrushes.Cell);
        }
    }

    // The border the selected unit wears.
    protected Pen SelectedCellBorder
    {
        get
        {
            return _selectedCellBorder ??= NoteBlock.BorderPen(CellBrushes.Selected);
        }
    }

    // Draws the background, the grid lines and the top border across what the viewer shows, then the lane's content.
    public override void Render(DrawingContext dc)
    {
        double width = RollMetrics.GridWidth(_song);
        dc.DrawRectangle(DrawnSurface.Background, null, new Rect(0, 0, width, RowHeight));
        var (left, viewRight) = Viewport;
        double right = _view.HasLength ? viewRight + CellWidth : viewRight;
        foreach (var (tick, isBarLine) in RollMetrics.GridLinesBetween(_song, left, right, GridEndTick))
        {
            double x = RollMetrics.XOfTick(tick);
            dc.DrawLine(isBarLine ? _barLine : _stepLine, new Point(x, 0), new Point(x, RowHeight));
        }

        dc.DrawLine(_barLine, new Point(0, 0), new Point(width, 0));
        if (_song is null)
        {
            return;
        }

        DrawContent(dc, left, right);
    }

    // The second value a region holds and where it fades to, null on every lane whose regions hold one.
    protected virtual (int Value, int? To)? SecondValueAt(int tick)
    {
        return null;
    }

    // Draws the regions, their dividers and their values, then the stretches a note decides over them, culled to a horizontal range.
    protected virtual void DrawContent(DrawingContext dc, double left, double right)
    {
        var dividerTicks = DrawnChanges.Select(change => change.Tick).ToHashSet();
        if (_isDragging)
        {
            dividerTicks.Add(_dragToTick);
        }

        foreach (var (region, x0, x1) in RegionsPlacedUpTo(RegionsDrawn(GridEndTick), right))
        {
            var (start, end, value, doesFade, fadeTo) = region;
            if (end <= start)
            {
                continue;
            }

            bool isSelected = GoverningDivider(dividerTicks, start) == (_isDragging ? _dragToTick : _selectedTick);
            if (start > 0 && dividerTicks.Contains(start) && x0 >= left && x0 <= right)
            {
                dc.DrawLine(isSelected ? SelectedDividerPen : DividerPen, new Point(x0, Space.Px1), new Point(x0, RowHeight - Space.Px1));
            }

            if (x1 < left)
            {
                continue;
            }

            double y0 = YOfValue(value);
            double y1 = doesFade ? YOfValue(fadeTo) : y0;
            dc.DrawGeometry(isSelected ? SelectedBandBrush : BandBrush, null, BandGeometry(x0, y0, x1, y1));
            var second = SecondValueAt(start);
            if (second is { } secondValue)
            {
                double secondY0 = YOfValue(secondValue.Value);
                double secondY1 = secondValue.To is int secondTo ? YOfValue(secondTo) : secondY0;
                dc.DrawLine(isSelected ? SelectedSecondLinePen : SecondLinePen, new Point(x0, secondY0), new Point(x1, secondY1));
            }

            dc.DrawLine(isSelected ? SelectedRegionLinePen : RegionLinePen, new Point(x0, y0), new Point(x1, y1));
            string text = ValueText(value, second?.Value) + (doesFade ? FadeArrow + ValueText(fadeTo, second?.To ?? second?.Value) : "");
            DrawVisibleValue(dc, x0, x1, text, isSelected ? CellBrushes.Selected : CellBrushes.Cell);
        }

        DrawOverriddenRegions(dc, left, right);
    }

    // The divider a drawn region answers to: the last one at or before it, or tick 0 where there is none.
    private static int GoverningDivider(HashSet<int> dividerTicks, int startTick)
    {
        int governingTick = 0;
        foreach (int tick in dividerTicks)
        {
            if (tick <= startTick && tick > governingTick)
            {
                governingTick = tick;
            }
        }

        return governingTick;
    }

    // The run of regions the row is drawn as, in tick order and reaching the end tick: the lane's own dividers over its base.
    protected virtual IEnumerable<DrawnRegion> RegionsDrawn(int endTick)
    {
        var fadesByTick = new Dictionary<int, (int Ticks, int To)>();
        foreach (var fade in DrawnFades)
        {
            if (fade.Ticks > 0)
            {
                fadesByTick[fade.Tick] = (fade.Ticks, fade.To);
            }
        }

        return RegionsBetweenDividers(BaseValue, DrawnChanges, fadesByTick, endTick);
    }

    // The regions a base and its dividers past tick 0 come to, each running to the next divider or the end tick and ramping where its fade says.
    protected static IEnumerable<DrawnRegion> RegionsBetweenDividers(int baseValue, IEnumerable<LaneValueChange> dividers,
        IReadOnlyDictionary<int, (int Ticks, int To)> fadesByTick, int endTick)
    {
        int startTick = 0;
        int value = baseValue;
        foreach (var divider in dividers)
        {
            if (divider.Tick <= 0 || divider.Tick >= endTick)
            {
                continue;
            }

            yield return RegionFrom(startTick, divider.Tick, value, fadesByTick);
            startTick = divider.Tick;
            value = divider.Value;
        }

        yield return RegionFrom(startTick, endTick, value, fadesByTick);
    }

    // One region between two ticks at a value, ramping where a fade starts at its first tick.
    private static DrawnRegion RegionFrom(int startTick, int endTick, int value, IReadOnlyDictionary<int, (int Ticks, int To)> fadesByTick)
    {
        bool doesFade = fadesByTick.TryGetValue(startTick, out var fade);
        return new DrawnRegion(startTick, endTick, value, doesFade, doesFade ? fade.To : 0);
    }

    // Draws the stretches a note decides over the lane's own line, each with its value where the lane writes one.
    protected void DrawOverriddenRegions(DrawingContext dc, double left, double right)
    {
        foreach (var (region, x0, x1) in RegionsPlacedUpTo(OverriddenRegions, right))
        {
            if (x1 < left)
            {
                continue;
            }

            dc.DrawLine(OverriddenPen, new Point(x0, YOfValue(region.Value)), new Point(x1, YOfValue(region.DoesFade ? region.FadeTo : region.Value)));
            DrawOverriddenValue(dc, region, x0, x1);
        }
    }

    // Regions in tick order, each with where it starts and ends along the lane, up to the last one starting at or before a right edge.
    private static IEnumerable<(DrawnRegion Region, double X0, double X1)> RegionsPlacedUpTo(IEnumerable<DrawnRegion> regions, double right)
    {
        foreach (var region in regions)
        {
            double x0 = RollMetrics.XOfTick(region.StartTick);
            if (x0 > right)
            {
                yield break;
            }

            yield return (region, x0, RollMetrics.XOfTick(region.EndTick));
        }
    }

    // Writes the value a stretch a note decides holds, and where it fades to, over the part of it on screen.
    protected virtual void DrawOverriddenValue(DrawingContext dc, DrawnRegion region, double x0, double x1)
    {
        string text = ValueText(region.Value, null) + (region.DoesFade ? FadeArrow + ValueText(region.FadeTo, null) : "");
        DrawVisibleValue(dc, x0, x1, text, OverriddenRegionBrush);
    }

    // A value as a region says it, with the second value beside it where there is one and it differs.
    private static string ValueText(int value, int? secondValue)
    {
        string text = value.ToString(CultureInfo.InvariantCulture);
        if (secondValue is not int second || second == value)
        {
            return text;
        }

        return text + SecondValueSeparator + second.ToString(CultureInfo.InvariantCulture);
    }

    // Draws a value centered on as much of a horizontal span as the view is showing.
    private void DrawVisibleValue(DrawingContext dc, double x0, double x1, string text, IBrush brush)
    {
        var (from, to) = VisibleSpan(x0, x1);
        if (to <= from)
        {
            return;
        }

        DrawValue(dc, text, new Rect(from, 0, to - from, RowHeight), brush);
    }

    // Where a region's value is drawn, which is also where its editor sits.
    private double RegionLabelX(int startTick)
    {
        int endTick = GridEndTick;
        foreach (var change in DrawnChanges)
        {
            if (change.Tick > startTick && change.Tick < endTick)
            {
                endTick = change.Tick;
            }
        }

        return VisibleCenterX(RollMetrics.XOfTick(startTick), RollMetrics.XOfTick(endTick));
    }

    // The band under a line from one point to another, down to the foot of the row.
    private StreamGeometry BandGeometry(double x0, double y0, double x1, double y1)
    {
        return StraightEdges.Filled(new Point(x0, y0), new Point(x1, y1), new Point(x1, RowHeight - Space.Px2), new Point(x0, RowHeight - Space.Px2));
    }

    // Where a value sits up the lane, the top of the range at the top of the row.
    protected double YOfValue(int value)
    {
        double span = Math.Max(ValueRange.Max - ValueRange.Min, 1);
        double fraction = Math.Clamp((value - ValueRange.Min) / span, 0, 1);
        return Space.Px2 + (1 - fraction) * (RowHeight - 2 * Space.Px2);
    }

    // The same brush's color at a lower opacity.
    private static IBrush WithOpacity(IBrush brush, double opacity)
    {
        return new ImmutableSolidColorBrush(brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent, opacity);
    }

    // Draws a value in the middle of its box, or nothing where the whole of it does not fit.
    protected static void DrawValue(DrawingContext dc, string text, Rect box, IBrush brush)
    {
        var formatted = AppFonts.UiText(text, ValueFontSize, brush);
        if (formatted.Width > box.Width - Space.Px4)
        {
            return;
        }

        dc.DrawText(formatted, new Point(box.X + (box.Width - formatted.Width) / 2, box.Y + (box.Height - formatted.Height) / 2));
    }

}
