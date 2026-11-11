using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The bar above the piano roll: its ticks and bar numbers and the wash past the end, and the presses that drag the playback marker and the loop and end markers' flags.
public sealed class TimeRuler : Control
{
    private readonly WindowCursor _windowCursor = new();
    private Song? _song;
    private Marker? _draggedMarker;
    private bool _hasPointerCapture;
    private int _lastRaisedTick = -1;
    private readonly ViewSpan _view = new();
    private bool _isCountingTicks;

    // Raised with a tick when the user moves the playback position.
    public event Action<int>? PositionRequested;

    // Raised with the tick a flag is being dragged onto, as the drag moves.
    public event Action<Marker, int>? MarkerMoved;

    // Raised once the flag has been dropped, which is the undo step.
    public event Action? MarkerDropped;

    // Raised with where a drag has got to, in the ruler's own coordinates, whether or not it reached a different tick.
    public event Action<double>? DragAt;

    // Raised when a drag ends, whichever of the three lines it was moving.
    public event Action? DragEnded;

    // Raised by a right-click on the end flag, which turns the marker on or off.
    public event Action? EndToggled;

    // Whether one of the two flags is being dragged.
    public bool IsDraggingMarker
    {
        get
        {
            return _draggedMarker is not null;
        }
    }

    // The song whose markers the ruler draws; setting it redraws the ruler.
    public Song? Song
    {
        get
        {
            return _song;
        }
        set
        {
            _song = value;
            Refresh();
        }
    }

    // Whether the bar lines are numbered with their tick rather than their bar; setting it redraws the ruler.
    public bool IsCountingTicks
    {
        get
        {
            return _isCountingTicks;
        }
        set
        {
            _isCountingTicks = value;
            InvalidateVisual();
        }
    }

    // Whether a wheel-button press with these keys held drags one of the three lines.
    public static bool DoesWheelPressDragLine(KeyModifiers keys)
    {
        return (keys & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt)) != 0;
    }

    // Takes a wheel-button press over the roll or a lane: a drag of one of the three lines where a modifier is held, and a pan of the view otherwise.
    public static void AnswerViewWheelPress(PointerPressedEventArgs e, Action<PointerPressedEventArgs> dragLine, Action<PointerPressedEventArgs> beginPan)
    {
        e.Handled = true;
        if (DoesWheelPressDragLine(e.KeyModifiers))
        {
            dragLine(e);
            return;
        }

        beginPan(e);
    }

    // Whether the end flag is drawn pointing right, which it is only too near the ruler's start to point left.
    public static bool DoesEndFlagPointRight(double endX)
    {
        return endX < RulerBand.FlagSize;
    }

    // Measures and redraws the ruler after the song has changed from outside.
    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    // Takes the stretch of the ruler the viewer is showing, redrawing only where it moved.
    public void SetViewport(double left, double width)
    {
        if (_view.MoveTo(left, width))
        {
            InvalidateVisual();
        }
    }

    // Starts a drag of the line a modifier names from a wheel-button press here, over the roll or over the lanes: Shift the playback position, Ctrl the loop marker and Alt the end marker.
    public void BeginWheelDrag(PointerPressedEventArgs e)
    {
        if (_song is null || _hasPointerCapture || !DoesWheelPressDragLine(e.KeyModifiers))
        {
            return;
        }

        _windowCursor.Remember(this, e);
        _draggedMarker = WheelDrag.IsPlayheadPress(e) ? null : e.KeyModifiers.HasFlag(KeyModifiers.Control) ? Marker.Loop : Marker.End;
        TakePointer(e);
        Follow(e.GetPosition(this).X);
    }

    // Re-runs the drag from where the cursor now is over the ruler, and answers whether a drag is running.
    public bool FollowCursor()
    {
        return _windowCursor.FollowOver(this, _hasPointerCapture, cursor => Follow(cursor.X));
    }

    // Draws the background, the ticks and bar numbers the viewer shows and the wash past the end.
    public override void Render(DrawingContext dc)
    {
        double width = RollMetrics.GridWidth(_song);
        RulerBand.DrawBackground(dc, width);
        DrawTicks(dc, width, (_song?.RollColumnCount ?? 0) * Song.GridTicks);
        if (_song is not null)
        {
            DrawPastEnd(dc, _song, width);
        }
    }

    // Asks to be as wide as the roll's surface.
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(RollMetrics.SurfaceWidth(_song), RulerBand.Height);
    }

    // Toggles the end marker on a right press on its flag, starts a wheel-button drag, or takes hold of a flag or the playback position.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        double x = e.GetPosition(this).X;
        _windowCursor.Remember(this, e);
        var buttons = e.GetCurrentPoint(this).Properties;
        if (!_hasPointerCapture && buttons.IsRightButtonPressed && MarkerAt(x) == Marker.End)
        {
            EndToggled?.Invoke();
            return;
        }

        if (!_hasPointerCapture && buttons.IsMiddleButtonPressed)
        {
            BeginWheelDrag(e);
            return;
        }

        if (!_hasPointerCapture)
        {
            _draggedMarker = MarkerAt(x);
            TakePointer(e);
        }

        Follow(x);
    }

    // Carries a running drag to where the pointer moved.
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _windowCursor.Remember(this, e);
        if (_hasPointerCapture)
        {
            Follow(e.GetPosition(this).X);
        }
    }

    // Lets the pointer go once the last button is up.
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!PointerButtons.IsAnyHeld(e.GetCurrentPoint(this).Properties))
        {
            e.Pointer.Capture(null);
        }
    }

    // Ends the drag, and drops a dragged flag as one undo step.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _hasPointerCapture = false;
        DragEnded?.Invoke();
        if (_draggedMarker is null)
        {
            return;
        }

        _draggedMarker = null;
        MarkerDropped?.Invoke();
    }

    // Takes the pointer for a drag that has not yet raised a tick.
    private void TakePointer(PointerPressedEventArgs e)
    {
        _lastRaisedTick = -1;
        _hasPointerCapture = true;
        e.Pointer.Capture(this);
    }

    // Moves whatever the press took hold of to the snap boundary nearest a position, raising where it landed.
    private void Follow(double x)
    {
        if (_song is null)
        {
            return;
        }

        int tick = RollMetrics.SnappedTickAtX(_song, x);
        tick = _draggedMarker == Marker.End ? Math.Max(0, tick) : Math.Clamp(tick, 0, _song.RollLengthTicks);
        DragAt?.Invoke(x);
        if (tick == _lastRaisedTick)
        {
            return;
        }

        _lastRaisedTick = tick;
        if (_draggedMarker is Marker marker)
        {
            MarkerMoved?.Invoke(marker, tick);
            return;
        }

        PositionRequested?.Invoke(tick);
    }

    // The flag a press at a position takes hold of, or null for the playback position.
    private Marker? MarkerAt(double x)
    {
        if (_song is null)
        {
            return null;
        }

        double endX = RollMetrics.XOfTick(_song.EndTick);
        bool doesEndPointRight = DoesEndFlagPointRight(endX);
        if (doesEndPointRight && RulerBand.IsFlagGrabbed(x, endX, doesPointRight: true))
        {
            return Marker.End;
        }

        if (RulerBand.IsFlagGrabbed(x, RollMetrics.XOfTick(_song.LoopTick), doesPointRight: true))
        {
            return Marker.Loop;
        }

        if (!doesEndPointRight && RulerBand.IsFlagGrabbed(x, endX, doesPointRight: false))
        {
            return Marker.End;
        }

        return null;
    }

    // Draws the ticks and bar numbers across the stretch the viewer shows.
    private void DrawTicks(DrawingContext dc, double width, int endTick)
    {
        var (left, right) = _view.VisibleRange(width);
        double leftOfNumberedLine = left - RollMetrics.BarStepTicks(_song) * RollMetrics.PixelsPerTick;
        foreach (var (tick, isBarLine) in RollMetrics.GridLinesBetween(_song, leftOfNumberedLine, right, endTick))
        {
            double x = RollMetrics.XOfTick(tick);
            if (!isBarLine)
            {
                RulerBand.DrawMinorTick(dc, x);
                continue;
            }

            RulerBand.DrawLabeledLine(dc, x, tick < endTick ? BarLineLabel(tick) : null);
        }
    }

    // What a bar line at a tick is numbered with: the tick itself while the ruler counts ticks, the bar's number otherwise.
    private string BarLineLabel(int tick)
    {
        int number = _isCountingTicks ? tick : tick / RollMetrics.TicksPerBarOf(_song) + 1;
        return number.ToString(CultureInfo.InvariantCulture);
    }

    // Draws the wash past the end of the song.
    private static void DrawPastEnd(DrawingContext dc, Song song, double width)
    {
        double endX = RollMetrics.EndMarkerX(song, width);
        dc.DrawRectangle(RollMetrics.PastEndBrush, null, new Rect(endX, 0, width - endX, RulerBand.Height));
    }

    // Which of the ruler's two draggable lines a drag is moving.
    public enum Marker
    {
        Loop,
        End,
    }
}
