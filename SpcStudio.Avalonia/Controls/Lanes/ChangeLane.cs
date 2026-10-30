using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// A one-row lane under the piano roll holding one of the song's values as it changes over time: regions between dividers where the value is in force at every tick, or the units a lane of units
// overrides it with. This part holds the lane's settings, its values, its selection and its geometry.
public abstract partial class ChangeLane : Control
{
    private double _laneHeight = RollMetrics.SnapHeight(RollMetrics.BaseLaneHeight);
    private bool _isClosedBelow;
    private Song? _song;
    private int? _selectedTick;
    private readonly ViewSpan _view = new();

    public event Action<ChangeLane, int>? PlaceRequested;
    public event Action<ChangeLane, int>? RemoveRequested;
    public event Action<ChangeLane, int, int>? MoveRequested;
    public event Action<ChangeLane, int?>? DragMoved;
    public event Action? RollResized;
    public event Action<double>? DragAt;
    public event Action? DragEnded;
    public event Action<Vector>? PanRequested;
    public event Action<PointerPressedEventArgs>? WheelDragRequested;
    public event Action<ChangeLane, int?>? SelectionChanged;

    // Takes the two brushes the lane's cells are drawn in.
    protected ChangeLane(LaneCellBrushes cellBrushes)
    {
        CellBrushes = cellBrushes;
    }

    // How wide one grid column's cell is drawn.
    protected static double CellWidth
    {
        get
        {
            return RollMetrics.CellWidth;
        }
    }

    // How tall this lane is drawn, held to the lane limits and snapped to a whole device pixel.
    public double LaneHeight
    {
        get
        {
            return _laneHeight;
        }
        set
        {
            double snapped = RollMetrics.SnapHeight(Math.Clamp(value, RollMetrics.MinLaneHeight, RollMetrics.MaxLaneHeight));
            if (_laneHeight == snapped)
            {
                return;
            }

            _laneHeight = snapped;
            Refresh();
        }
    }

    // Whether the line closing this lane is drawn by the surface under it rather than by the lane after it.
    public bool IsClosedBelow
    {
        get
        {
            return _isClosedBelow;
        }
        set
        {
            if (_isClosedBelow == value)
            {
                return;
            }

            _isClosedBelow = value;
            Refresh();
        }
    }

    // The height the row is drawn at, a pixel short of the lane where the surface under it closes it.
    protected double RowHeight
    {
        get
        {
            return LaneHeight - (IsClosedBelow ? RollMetrics.DevicePixel : 0);
        }
    }

    // The song being edited; setting it drops the selection and redraws.
    public Song? Song
    {
        get
        {
            return _song;
        }
        set
        {
            _song = value;
            _selectedTick = null;
            OnSongReplaced();
            Refresh();
        }
    }

    // The changes to draw, in tick order.
    protected abstract IEnumerable<LaneValueChange> Changes { get; }

    // The fades laid over those changes, each as its start, its length and the value it fades to: the fading ones of the lane's own regions.
    private IEnumerable<(int Tick, int Ticks, int To)> Fades
    {
        get
        {
            if (_song is null)
            {
                return [];
            }

            return OwnRegions(_song).Where(region => region.Fades).Select(region => (region.Start, region.LengthTicks, region.FadeTo));
        }
    }

    // Whether the lane's values belong to one channel rather than to the song.
    public virtual bool IsPerChannel
    {
        get
        {
            return false;
        }
    }

    // Whether this lane's command can fade.
    public virtual bool CanFade
    {
        get
        {
            return false;
        }
    }

    // Whether the lane is drawn as regions between dividers, which every lane is but a lane of units.
    public virtual bool IsDrawnAsRegions
    {
        get
        {
            return true;
        }
    }

    // The value the first region holds, which no change says.
    public abstract int BaseValue { get; }

    // The value the first region holds in a song that has set nothing on the lane.
    public abstract int DefaultValue { get; }

    // Whether this lane holds a divider or a unit at all, which decides whether it is shown when a song is opened.
    public virtual bool HasChanges
    {
        get
        {
            return Changes.Any();
        }
    }

    public abstract ValueRange ValueRange { get; }
    protected LaneCellBrushes CellBrushes { get; }

    // How many columns the roll has.
    private int RollColumnCount
    {
        get
        {
            return _song?.RollColumnCount ?? 0;
        }
    }

    // The tick the roll's last column ends at.
    private int GridEndTick
    {
        get
        {
            return RollColumnCount * Song.GridTicks;
        }
    }

    public int? SelectedTick
    {
        get
        {
            return _selectedTick;
        }
    }

    // What the viewer is showing, in the lane's own coordinates, or the whole grid before the viewer has said.
    protected (double Left, double Right) Viewport
    {
        get
        {
            return _view.VisibleRange(RollMetrics.GridWidth(_song));
        }
    }

    // The changes as they are drawn: the song's own, with the one being dragged moved to where the drag has got to.
    private IEnumerable<LaneValueChange> DrawnChanges
    {
        get
        {
            if (!_isDragging || ChangeValueAt(_dragFromTick) is not int value)
            {
                return Changes;
            }

            return Changes
                .Where(change => change.Tick != _dragFromTick && change.Tick != _dragToTick)
                .Append(new LaneValueChange(_dragToTick, value))
                .OrderBy(change => change.Tick);
        }
    }

    // The fades as they are drawn, the one being dragged travelling with its divider.
    private IEnumerable<(int Tick, int Ticks, int To)> DrawnFades
    {
        get
        {
            if (!_isDragging)
            {
                return Fades;
            }

            var others = Fades.Where(fade => fade.Tick != _dragFromTick && fade.Tick != _dragToTick);
            return FadeAt(_dragFromTick) is (int ticks, int to) ? others.Append((Tick: _dragToTick, Ticks: ticks, To: to)) : others;
        }
    }

    // The value a change at a tick holds, or null where that tick carries none.
    public abstract int? ChangeValueAt(int tick);

    // The regions the lane's own changes make in a song, which its fades are read off; none on a lane whose command has no fade form.
    protected virtual IEnumerable<LaneRegion> OwnRegions(Song song)
    {
        return [];
    }

    // The value the region starting at a tick holds: the change's own, or the base in the first region.
    public int RegionValue(int tick)
    {
        return tick == 0 ? BaseValue : ChangeValueAt(tick) ?? BaseValue;
    }

    // The value a right-click puts the region starting at a tick back at: the region before it's, and the lane's default for the first region.
    public int RegionResetValue(int tick)
    {
        if (tick == 0)
        {
            return DefaultValue;
        }

        int previousValue = BaseValue;
        foreach (var change in Changes)
        {
            if (change.Tick >= tick)
            {
                break;
            }

            previousValue = change.Value;
        }

        return previousValue;
    }

    // The value the region after the one starting at a tick opens on, or null where that region is the last.
    public int? NextRegionValue(int tick)
    {
        foreach (var change in Changes)
        {
            if (change.Tick > tick)
            {
                return change.Value;
            }
        }

        return null;
    }

    // The fade the change at a tick carries, or null where it carries none.
    public (int Ticks, int To)? FadeAt(int tick)
    {
        foreach (var fade in Fades)
        {
            if (fade.Tick == tick)
            {
                return (fade.Ticks, fade.To);
            }
        }

        return null;
    }

    // Drops the selection without telling the window.
    public void ClearSelectionSilently()
    {
        if (_selectedTick is null)
        {
            return;
        }

        _selectedTick = null;
        InvalidateVisual();
    }

    // Puts the selection on a tick without telling the window.
    public void SelectSilently(int tick)
    {
        if (_selectedTick == tick)
        {
            return;
        }

        _selectedTick = tick;
        InvalidateVisual();
    }

    // Selects a region or a unit and tells the window, which opens its editor over it.
    protected void Select(int tick)
    {
        SelectSilently(tick);
        SelectionChanged?.Invoke(this, tick);
    }

    // Drops the selection and tells the window, whether or not this lane held one.
    protected void Deselect()
    {
        ClearSelectionSilently();
        SelectionChanged?.Invoke(this, null);
    }

    // Re-measures and redraws after the song has been changed from outside.
    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    // Lets a lane holding a selection of its own drop it when the song is replaced.
    protected virtual void OnSongReplaced()
    {
    }

    // Where the cell editor sits over the region starting at a tick, in the lane's own coordinates.
    public virtual Point CellTopCenter(int tick)
    {
        return new Point(_isDragging ? RollMetrics.XOfTick(tick) : RegionLabelX(tick), 0);
    }

    // Tells the lane what the viewer is showing, which is what drawing is culled to.
    public void SetViewport(double left, double width)
    {
        if (_view.MoveTo(left, width))
        {
            InvalidateVisual();
        }
    }

    // Asks to be as wide as the roll's surface and as tall as the lane's row.
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(RollMetrics.SurfaceWidth(_song), RowHeight);
    }

    // The middle of as much of a horizontal span as the view is showing, or of the span itself where none of it is on screen.
    protected double VisibleCenterX(double x0, double x1)
    {
        var (from, to) = VisibleSpan(x0, x1);
        return to > from ? (from + to) / 2 : (x0 + x1) / 2;
    }

    // As much of a horizontal span as the view is showing, ending at or before where it starts where none of it is on screen.
    private (double From, double To) VisibleSpan(double x0, double x1)
    {
        var (viewLeft, viewRight) = Viewport;
        return (Math.Max(x0, viewLeft), Math.Min(x1, viewRight));
    }
}
