using System.Globalization;
using Avalonia;
using Avalonia.Media;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The lane's drawing: the background and the grid, and the regions, their dividers, their values and the stretches a note decides.
public abstract partial class ChangeLane
{
    private const double GridLineThickness = 1;
    private const double ValueFontSize = 10;
    private const string FadeArrow = " > ";
    private const string SecondValueSeparator = "/";

    protected static readonly Brush OverriddenRegionBrush = new SolidColorBrush(Palette.LaneOverridden);
    private static readonly RegionLook OverriddenRegionLook = RegionLook.Of(OverriddenRegionBrush);
    private static readonly Pen LaneLineUnderOverridePen = DashedLine.PenOf(OverriddenRegionLook.Line.Brush!, OverriddenRegionLook.Line.Thickness);
    private static readonly Pen OverriddenDividerPen = new(OverriddenRegionBrush, GridLineThickness);
    private readonly Pen _stepLine = new(new SolidColorBrush(Palette.CharcoalGray), GridLineThickness);
    private readonly Pen _barLine = new(new SolidColorBrush(Palette.SteelGray), GridLineThickness);
    private RegionLook? _plainRegionLook;
    private RegionLook? _selectedRegionLook;
    private Pen? _dividerPen;
    private Pen? _selectedDividerPen;
    private Pen? _previewDividerPen;
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

    // What a region that is not selected is drawn with.
    private RegionLook PlainRegionLook
    {
        get
        {
            return _plainRegionLook ??= RegionLook.Of(CellBrushes.Cell);
        }
    }

    // What the selected region is drawn with.
    private RegionLook SelectedRegionLook
    {
        get
        {
            return _selectedRegionLook ??= RegionLook.Of(CellBrushes.Selected);
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

    // The line opening the selected region, and the divider being dragged.
    private Pen SelectedDividerPen
    {
        get
        {
            return _selectedDividerPen ??= new Pen(CellBrushes.Selected, GridLineThickness);
        }
    }

    // The dimmed line drawn where a right press would put a divider.
    private Pen PreviewDividerPen
    {
        get
        {
            return _previewDividerPen ??= new Pen(NoteBlock.PreviewLineBrush(NoteBlock.ColorOf(CellBrushes.Cell)), GridLineThickness);
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

    // Draws the regions, their dividers and their values, with the stretches a note decides drawn in their own color in place of the lane's, culled
    // to a horizontal range.
    protected virtual void DrawContent(DrawingContext dc, double left, double right)
    {
        var dividerTicks = Changes.Select(change => change.Tick).ToHashSet();
        var overriddenRegions = OverriddenRegions.ToList();
        foreach (var (region, _, x1) in RegionsPlacedUpTo(RegionsDrawn(GridEndTick), right))
        {
            if (region.EndTick <= region.StartTick)
            {
                continue;
            }

            bool isSelected = GoverningDivider(dividerTicks, region.StartTick) == _selectedTick;
            if (x1 < left)
            {
                continue;
            }

            var second = SecondRegionOf(region);
            var uncoveredParts = PartsWithin(UncoveredSpans(region.StartTick, region.EndTick, overriddenRegions), [region]);
            foreach (var (part, partX0, partX1) in RegionsPlacedUpTo(uncoveredParts, right))
            {
                if (partX1 < left)
                {
                    continue;
                }

                DrawRegion(dc, part, partX0, partX1, second?.ClippedTo(part.StartTick, part.EndTick), isSelected ? SelectedRegionLook : PlainRegionLook);
            }
        }

        DrawOverriddenRegions(dc, left, right, overriddenRegions);
        DrawLaneLinesUnderOverrides(dc, left, right, overriddenRegions);
        DrawDividers(dc, left, right, dividerTicks, overriddenRegions);
        if (_previewedDividerTick is int previewedTick)
        {
            DrawDivider(dc, PreviewDividerPen, RollMetrics.XOfTick(previewedTick));
        }
    }

    // The second value a region holds as a region of its own over the same ticks, or null where it holds none.
    private DrawnRegion? SecondRegionOf(DrawnRegion region)
    {
        if (SecondValueAt(region.StartTick) is not { } second)
        {
            return null;
        }

        return new DrawnRegion(region.StartTick, region.EndTick, second.Value, second.To is not null, second.To ?? second.Value);
    }

    // Draws one region's band, its second value's line where it has one, its line and its value, in one look.
    private void DrawRegion(DrawingContext dc, DrawnRegion region, double x0, double x1, DrawnRegion? second, RegionLook look)
    {
        double y0 = YOfValue(region.Value);
        double y1 = YOfEnd(region);
        dc.DrawGeometry(look.Band, null, BandGeometry(x0, y0, x1, y1));
        if (second is DrawnRegion secondRegion)
        {
            dc.DrawLine(look.SecondLine, new Point(x0, YOfValue(secondRegion.Value)), new Point(x1, YOfEnd(secondRegion)));
        }

        dc.DrawLine(look.Line, new Point(x0, y0), new Point(x1, y1));
        string text = ValueText(region.Value, second?.Value) + (region.DoesFade ? FadeArrow + ValueText(region.FadeTo, second?.FadeTo) : "");
        DrawVisibleValue(dc, x0, x1, text, look.Text);
    }

    // Draws the lane's own line dashed across every stretch a note decides, culled to a horizontal range.
    private void DrawLaneLinesUnderOverrides(DrawingContext dc, double left, double right, IReadOnlyList<DrawnRegion> overriddenRegions)
    {
        if (overriddenRegions.Count == 0)
        {
            return;
        }

        var overriddenSpans = overriddenRegions.Select(overridden => (overridden.StartTick, overridden.EndTick));
        foreach (var (part, x0, x1) in RegionsPlacedUpTo(PartsWithin(overriddenSpans, LaneOwnRegions(GridEndTick).ToList()), right))
        {
            if (x1 < left)
            {
                continue;
            }

            dc.DrawLine(LaneLineUnderOverridePen, new Point(x0, YOfValue(part.Value)), new Point(x1, YOfEnd(part)));
        }
    }

    // The parts of the regions inside each of the spans, span by span, a ramp keeping its own slope in each.
    protected static IEnumerable<DrawnRegion> PartsWithin(IEnumerable<(int Start, int End)> spans, IReadOnlyList<DrawnRegion> regions)
    {
        return spans.SelectMany(span => regions.Select(region => region.ClippedTo(span.Start, span.End)).OfType<DrawnRegion>());
    }

    // The stretches between two ticks that none of the stretches a note decides covers, those being in tick order.
    protected static IEnumerable<(int Start, int End)> UncoveredSpans(int startTick, int endTick, IReadOnlyList<DrawnRegion> overriddenRegions)
    {
        int uncoveredFrom = startTick;
        foreach (var overridden in overriddenRegions)
        {
            if (overridden.StartTick >= endTick)
            {
                break;
            }

            if (overridden.EndTick <= uncoveredFrom)
            {
                continue;
            }

            if (overridden.StartTick > uncoveredFrom)
            {
                yield return (uncoveredFrom, overridden.StartTick);
            }

            uncoveredFrom = Math.Max(uncoveredFrom, overridden.EndTick);
        }

        if (uncoveredFrom < endTick)
        {
            yield return (uncoveredFrom, endTick);
        }
    }

    // Draws the lane's dividers and the edges of the stretches a note decides, a divider such a stretch covers or bounds and every edge of one in the
    // override color, culled to a horizontal range.
    private void DrawDividers(DrawingContext dc, double left, double right, HashSet<int> dividerTicks, IReadOnlyList<DrawnRegion> overriddenRegions)
    {
        var overriddenDividerTicks = overriddenRegions.SelectMany(overridden => new[] { overridden.StartTick, overridden.EndTick }).ToHashSet();
        foreach (int tick in dividerTicks)
        {
            if (overriddenDividerTicks.Contains(tick) || overriddenRegions.Any(overridden => overridden.StartTick < tick && tick < overridden.EndTick))
            {
                overriddenDividerTicks.Add(tick);
                continue;
            }

            bool isHighlighted = tick == _selectedTick || _isDragging && tick == _dragToTick;
            DrawDividerInView(dc, isHighlighted ? SelectedDividerPen : DividerPen, tick, left, right);
        }

        foreach (int tick in overriddenDividerTicks)
        {
            DrawDividerInView(dc, OverriddenDividerPen, tick, left, right);
        }
    }

    // Draws a divider's line at a tick past the start and short of the end of the lane, where it falls in a horizontal range.
    private void DrawDividerInView(DrawingContext dc, Pen pen, int tick, double left, double right)
    {
        double x = RollMetrics.XOfTick(tick);
        if (tick <= 0 || tick >= GridEndTick || x < left || x > right)
        {
            return;
        }

        DrawDivider(dc, pen, x);
    }

    // Draws a divider's line down the row at a position.
    private void DrawDivider(DrawingContext dc, Pen pen, double x)
    {
        dc.DrawLine(pen, new Point(x, Space.Px1), new Point(x, RowHeight - Space.Px1));
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
        return LaneOwnRegions(endTick);
    }

    // The lane's own dividers over its base as regions, in tick order and reaching the end tick, whatever its notes decide.
    private IEnumerable<DrawnRegion> LaneOwnRegions(int endTick)
    {
        var fadesByTick = new Dictionary<int, (int Ticks, int To)>();
        foreach (var fade in Fades)
        {
            if (fade.Ticks > 0)
            {
                fadesByTick[fade.Tick] = (fade.Ticks, fade.To);
            }
        }

        return RegionsBetweenDividers(BaseValue, Changes, fadesByTick, endTick);
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

    // Draws the given stretches a note decides, culled to a horizontal range.
    protected void DrawOverriddenRegions(DrawingContext dc, double left, double right, IReadOnlyList<DrawnRegion> overriddenRegions)
    {
        foreach (var (region, x0, x1) in RegionsPlacedUpTo(overriddenRegions, right))
        {
            if (x1 < left)
            {
                continue;
            }

            DrawOverriddenRegion(dc, region, x0, x1);
        }
    }

    // Draws one stretch a note decides as a region in the override color.
    protected virtual void DrawOverriddenRegion(DrawingContext dc, DrawnRegion region, double x0, double x1)
    {
        DrawRegion(dc, region, x0, x1, null, OverriddenRegionLook);
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
        foreach (var change in Changes)
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

    // Where a region's line ends up the lane: at the value it fades to, or at its own where it holds flat.
    private double YOfEnd(DrawnRegion region)
    {
        return YOfValue(region.DoesFade ? region.FadeTo : region.Value);
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
