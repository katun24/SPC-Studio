using Avalonia;
using Avalonia.Media;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The piano roll's grid dimensions and zoom, which the roll, the ruler and the lanes all draw from.
public static class RollMetrics
{
    public const double KeyWidth = 56;
    public const double BaseCellWidth = 21;
    public const double BaseCellHeight = 20;
    public const double BaseLaneHeight = 26;
    public const double DefaultMinZoom = 0.25;
    public const double AbsoluteMinZoom = 1.0 / 4096;
    public const double MinVerticalZoom = 0.5;
    public const double MaxVerticalZoom = 2.5;
    public const double MinLaneScale = 0.75;
    public const double MaxLaneScale = 5;
    public const int MinZoomStepPercent = 5;
    public const int MaxZoomStepPercent = 50;
    public const int DefaultZoomStepPercent = 15;
    public const double GridOverhang = 1;
    public const double ViewportTolerance = 0.01;
    public const int MinPastEndTicks = SnesTiming.TicksPerHalfNote;
    private const double RowCenterFraction = 0.5;
    private const int MaxGridStepBarCount = 4096;

    // The furthest in the horizontal zoom goes, where the grid draws a line every tick at the least spacing grid lines keep.
    public const double MaxZoom = Space.Px10 * Song.GridTicks / BaseCellWidth;

    public static readonly IBrush PastEndBrush = new SolidColorBrush(Palette.PastEndWash);
    public static readonly bool[] IsBlackKey = [false, true, false, true, false, false, true, false, true, false, true, false];
    private static readonly int MaxGridStepTicks = TimeSignature.LongestBarTicks * MaxGridStepBarCount;
    private static double _minZoom = DefaultMinZoom;
    private static double _zoom = 1;
    private static int _zoomStepPercent = DefaultZoomStepPercent;
    private static double _verticalZoom = 1;
    private static double _renderScaling = 1;
    private static double _fillWidth;

    // The floor under the horizontal zoom, which is a song's own, held between the hard stop and the default floor.
    public static double MinZoom
    {
        get
        {
            return _minZoom;
        }
        set
        {
            _minZoom = Math.Clamp(value, AbsoluteMinZoom, DefaultMinZoom);
        }
    }

    // The horizontal zoom, held inside its range.
    public static double Zoom
    {
        get
        {
            return _zoom;
        }
        set
        {
            _zoom = Math.Clamp(value, MinZoom, MaxZoom);
        }
    }

    // The zoom speed in percent a wheel notch, held inside its range.
    public static int ZoomStepPercent
    {
        get
        {
            return _zoomStepPercent;
        }
        set
        {
            _zoomStepPercent = Math.Clamp(value, MinZoomStepPercent, MaxZoomStepPercent);
        }
    }

    // The vertical zoom, held inside its range.
    public static double VerticalZoom
    {
        get
        {
            return _verticalZoom;
        }
        set
        {
            _verticalZoom = Math.Clamp(value, MinVerticalZoom, MaxVerticalZoom);
        }
    }

    // The width of one grid column as it is drawn now.
    public static double CellWidth
    {
        get
        {
            return BaseCellWidth * Zoom;
        }
    }

    // The height of one row as it is drawn now.
    public static double CellHeight
    {
        get
        {
            return BaseCellHeight * VerticalZoom;
        }
    }

    // The smallest a lane row is drawn, snapped to a whole device pixel.
    public static double MinLaneHeight
    {
        get
        {
            return SnapHeight(BaseLaneHeight * MinLaneScale);
        }
    }

    // The largest a lane row is drawn, snapped to a whole device pixel.
    public static double MaxLaneHeight
    {
        get
        {
            return SnapHeight(BaseLaneHeight * MaxLaneScale);
        }
    }

    // The window's device pixel ratio, which is never zero or less.
    public static double RenderScaling
    {
        get
        {
            return _renderScaling;
        }
        set
        {
            _renderScaling = value > 0 ? value : 1;
        }
    }

    // One device pixel in the layout's own units.
    public static double DevicePixel
    {
        get
        {
            return 1 / RenderScaling;
        }
    }

    // How tall the roll's grid is drawn, one row per semitone.
    public static double GridHeight
    {
        get
        {
            return Song.RollRowCount * CellHeight;
        }
    }

    // How many pixels one tick is drawn across.
    public static double PixelsPerTick
    {
        get
        {
            return CellWidth / Song.GridTicks;
        }
    }

    // Where a tick sits along the roll, measured from its start.
    public static double XOfTick(double tick)
    {
        return tick * PixelsPerTick;
    }

    // What a run of wheel notches multiplies a zoom by, at the zoom speed.
    public static double ZoomFactorForNotches(double notches)
    {
        return Math.Pow(1 + ZoomStepPercent / Percent.PerWhole, notches);
    }

    // Where a view's start goes, in pixels across what it scrolls over, so that what stood under a point of the view stands there still once the scale has changed.
    public static double ViewStartHolding(double viewStart, double pointInView, double scaleBefore, double scaleAfter)
    {
        return (viewStart + pointInView) / scaleBefore * scaleAfter - pointInView;
    }

    // Puts the zoom's floor where the whole song fits a viewer and holds the zoom to it, answering whether the zoom moved.
    public static bool UpdateMinZoom(Song? song, double viewportWidth)
    {
        MinZoom = ZoomFillingViewport(song, viewportWidth) ?? DefaultMinZoom;
        double zoomBefore = _zoom;
        _zoom = Math.Clamp(_zoom, MinZoom, MaxZoom);
        return _zoom != zoomBefore;
    }

    // The zoom at which the whole song fills a viewer, held inside the zoom's range, or the current zoom where there is nothing to fit.
    public static double FitZoom(Song? song, double viewportWidth)
    {
        return ZoomFillingViewport(song, viewportWidth) is double fit ? Math.Clamp(fit, MinZoom, MaxZoom) : Zoom;
    }

    // A height rounded to a whole device pixel.
    public static double SnapHeight(double height)
    {
        return Math.Round(height * RenderScaling, MidpointRounding.AwayFromZero) / RenderScaling;
    }

    // How wide the grid is drawn for a song.
    public static double GridWidth(Song? song)
    {
        return (song?.RollColumnCount ?? 0) * CellWidth;
    }

    // How wide the roll, the ruler and the lanes are laid out for a song: the grid and an empty space past its end, as far as the viewer shows and never narrower than the shortest that space may be.
    public static double SurfaceWidth(Song? song)
    {
        return Math.Max(GridWidth(song) + MinPastEndTicks * PixelsPerTick, _fillWidth);
    }

    // Takes the width the roll's viewer gives the surfaces to fill, answering whether it changed.
    public static bool UpdateFillWidth(double fillWidth)
    {
        if (IsSameViewportValue(_fillWidth, fillWidth))
        {
            return false;
        }

        _fillWidth = fillWidth;
        return true;
    }

    // Covers the empty space past the end of the grid with a fill that shows nothing, so a surface takes the pointer there.
    public static void DrawPointerSpacePastEnd(DrawingContext dc, Song? song, double height)
    {
        double gridWidth = GridWidth(song);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(gridWidth, 0, SurfaceWidth(song) - gridWidth, height));
    }

    // Where the song's end marker is drawn, held to the grid's width, which a marker at the very end can land a rounding error past.
    public static double EndMarkerX(Song song, double gridWidth)
    {
        return Math.Min(XOfTick(song.EndTick), gridWidth);
    }

    // Whether two viewport positions or sizes are close enough to count as the same.
    public static bool IsSameViewportValue(double current, double next)
    {
        return Math.Abs(current - next) < ViewportTolerance;
    }

    // Which step of a given width a position falls in, counting from the grid's start, negative positions included.
    public static int StepIndexAt(double position, double stepWidth)
    {
        return (int)Math.Floor(position / stepWidth);
    }

    // The tick a horizontal position on the grid stands at, fractions and positions off either end included.
    public static double ExactTickAtX(double x)
    {
        return x / PixelsPerTick;
    }

    // The tick a horizontal position on the grid falls on, off either end included.
    public static int TickAtX(double x)
    {
        return (int)ExactTickAtX(x);
    }

    // The snap step's tick nearest a horizontal position on the grid.
    public static int SnappedTickAtX(Song song, double x)
    {
        return song.SnapToNearest((int)Math.Round(ExactTickAtX(x)));
    }

    // How far a drag from one horizontal position to another moves something, in ticks, counted in whole snap steps between the steps the two positions fall in.
    public static int SnapStepsMovedTicks(Song song, double fromX, double toX)
    {
        double snapStepWidth = song.SnapTicks * PixelsPerTick;
        return (StepIndexAt(toX, snapStepWidth) - StepIndexAt(fromX, snapStepWidth)) * song.SnapTicks;
    }

    // Finds the row of the roll a height on it falls in, answering false above or below the roll.
    public static bool TryGetRowAt(double y, out int row)
    {
        row = StepIndexAt(y, CellHeight);
        return row >= 0 && row < Song.RollRowCount;
    }

    // The semitone a row of the roll stands for, row 0 being the top and so the highest pitch.
    public static int SemitoneOfRow(int row)
    {
        return Song.HighestSemitone - row;
    }

    // The row of the roll a semitone stands on.
    public static int RowOfSemitone(int semitone)
    {
        return Song.HighestSemitone - semitone;
    }

    // The vertical center of where a pitch stands on the roll, a pitch between two rows included.
    public static double PitchCenterY(double semitone)
    {
        return (Song.HighestSemitone - semitone + RowCenterFraction) * CellHeight;
    }

    // How many ticks apart the plain grid lines are drawn: the snap size, doubled until they are far enough apart to read.
    public static int GridStepTicks(Song? song)
    {
        return StepTicksSpacedAtLeast(song?.SnapTicks ?? Song.GridTicks, TicksPerBarOf(song), Space.Px10);
    }

    // How many ticks apart the bar lines and the ruler's numbers are drawn: a bar, doubled until they are far enough apart to read.
    public static int BarStepTicks(Song? song)
    {
        int ticksPerBar = TicksPerBarOf(song);
        return StepTicksSpacedAtLeast(ticksPerBar, ticksPerBar, Space.Px40);
    }

    // The ticks a surface draws its vertical grid lines at between two positions and up to an end tick, each with whether it is a bar line: every
    // plain step and every bar step, in order, a tick on both being a bar line.
    public static IEnumerable<(int Tick, bool IsBarLine)> GridLinesBetween(Song? song, double left, double right, int endTick)
    {
        int gridStepTicks = GridStepTicks(song);
        int barStepTicks = BarStepTicks(song);
        int leftTick = Math.Max(0, (int)(left / PixelsPerTick));
        int lastTick = Math.Min(endTick, (int)(right / PixelsPerTick) + Math.Min(gridStepTicks, barStepTicks));
        int gridTick = leftTick / gridStepTicks * gridStepTicks;
        int barTick = leftTick / barStepTicks * barStepTicks;
        for (int tick = Math.Min(gridTick, barTick); tick <= lastTick; tick = Math.Min(gridTick, barTick))
        {
            yield return (tick, tick == barTick);
            if (tick == gridTick)
            {
                gridTick += gridStepTicks;
            }

            if (tick == barTick)
            {
                barTick += barStepTicks;
            }
        }
    }

    // Where the playback marker stands across a surface of a width, or null where it is hidden or past either edge.
    public static double? PlayheadX(double? playheadStep, double surfaceWidth)
    {
        if (playheadStep is not double step || step < 0)
        {
            return null;
        }

        double x = step * CellWidth;
        return x > surfaceWidth ? null : x;
    }

    // The zoom at which the grid drawn for a song and the shortest space past its end are exactly a viewer's width, or null for a song of no length or an unmeasured viewer.
    private static double? ZoomFillingViewport(Song? song, double viewportWidth)
    {
        int rollTicks = song?.RollLengthTicks ?? 0;
        if (rollTicks <= 0 || viewportWidth <= 0)
        {
            return null;
        }

        return viewportWidth / ((rollTicks + MinPastEndTicks) * BaseCellWidth / Song.GridTicks);
    }

    // How many ticks a song's bar lasts, or a bar of the default time signature where there is no song.
    public static int TicksPerBarOf(Song? song)
    {
        return (song?.TimeSignature ?? TimeSignature.Default).TicksPerBar;
    }

    // A tick count doubled until the lines it puts on the grid are at least a spacing apart, landing on a bar where a doubling would not divide one.
    private static int StepTicksSpacedAtLeast(int ticks, int ticksPerBar, double minSpacing)
    {
        int stepTicks = Math.Max(1, ticks);
        double pixelsPerTick = PixelsPerTick;
        while (stepTicks * pixelsPerTick < minSpacing && stepTicks < MaxGridStepTicks)
        {
            stepTicks = NextStepTicks(stepTicks, ticksPerBar);
        }

        return stepTicks;
    }

    // The step after a step: doubled past a bar, and below one doubled only where the double still divides a bar, the bar itself otherwise.
    private static int NextStepTicks(int stepTicks, int ticksPerBar)
    {
        if (stepTicks >= ticksPerBar)
        {
            return stepTicks * 2;
        }

        int doubledTicks = stepTicks * 2;
        return doubledTicks < ticksPerBar && ticksPerBar % doubledTicks == 0 ? doubledTicks : ticksPerBar;
    }
}
