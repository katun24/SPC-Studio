using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SpcStudio.Audio;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// An envelope's rows drawn as one curve by running them, a line where each row after the first takes over, and on the open row three handles that
// drag its ADSR registers to the values the DSP can play.
public sealed class AdsrGraph : Control
{
    private const double HandleRadius = 4.5;
    private const double GrabRadius = 11;
    private const double CurveThickness = 2;
    private const int SustainLevelShift = 8;

    private readonly Brush _handleFill = new SolidColorBrush(Palette.Amber);
    private readonly Pen _curvePen = new Pen(EnvelopeCurve.LineBrush, CurveThickness);
    private readonly Pen _handleEdgePen = new Pen(new SolidColorBrush(Palette.EnvelopeHandleEdge), 1);
    private object? _owner;
    private IReadOnlyList<EnvelopeChange> _rows = [];
    private int? _openRowIndex;
    private Handle _draggedHandle;

    // The handles in the order they are hit-tested, so a tie goes to the one listed last.
    private static readonly Handle[] HandlesInHitTestOrder = [Handle.Sustain, Handle.Attack, Handle.Decay];

    // The rows run as one curve, or null with no rows.
    private EnvelopeTrace? _trace;

    // Raised when a drag has moved one of the open row's registers, with the handle that moved and the change to apply to that row's envelope.
    public event Action<object, Func<EnvelopeState, EnvelopeState>>? Edited;

    // Whether a row is open and runs ADSR, which is what puts handles on it.
    private bool HasHandles
    {
        get
        {
            return _openRowIndex is int openRowIndex && _rows[openRowIndex].Registers.UsesAdsr;
        }
    }

    // The right edge of the area the curve is drawn in.
    private double PlotRight
    {
        get
        {
            return EnvelopeTimeAxis.PlotRight(Bounds.Width);
        }
    }

    // The bottom edge of the area the curve is drawn in, which is silence.
    private double PlotBottom
    {
        get
        {
            return EnvelopeCurve.PlotBottom(Bounds.Height);
        }
    }

    // Points the graph at an envelope's rows, each at the time it takes over, and at the row whose handles it shows, or none.
    public void Bind(object? owner, IReadOnlyList<EnvelopeChange> rows, int? openRowIndex)
    {
        if (!Equals(_owner, owner))
        {
            _owner = owner;
            _draggedHandle = Handle.None;
        }

        _rows = rows;
        _openRowIndex = openRowIndex;
        _trace = EnvelopeCurve.TraceOf(rows);
        InvalidateVisual();
    }

    // Draws the background and the lines framing the plot and marking its times, then the curve with a line where each later row takes over, and the open row's sustain line and handles.
    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(DrawnSurface.Background, null, new Rect(0, 0, Bounds.Width, Bounds.Height));
        DrawTimeGrid(context);
        if (_trace is not { } trace)
        {
            return;
        }

        DrawRowStarts(context);
        if (HasHandles)
        {
            DrawSustainLine(context);
        }

        EnvelopeCurve.Draw(context, trace, Bounds.Size, _curvePen);
        if (HasHandles)
        {
            DrawHandles(context, trace);
        }
    }

    // Shows a hand over a handle, or moves the handle being dragged to the pointer.
    protected override void OnPointerMoved(PointerEventArgs moveArgs)
    {
        base.OnPointerMoved(moveArgs);
        if (!HasHandles)
        {
            return;
        }

        if (_draggedHandle == Handle.None)
        {
            Cursor = HandleNearest(moveArgs.GetPosition(this)) == Handle.None ? null : new Cursor(StandardCursorType.Hand);
            return;
        }

        DragHandleTo(moveArgs.GetPosition(this));
    }

    // Starts dragging the handle a left press lands on.
    protected override void OnPointerPressed(PointerPressedEventArgs pressArgs)
    {
        base.OnPointerPressed(pressArgs);
        if (!pressArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !HasHandles)
        {
            return;
        }

        _draggedHandle = HandleNearest(pressArgs.GetPosition(this));
        if (_draggedHandle == Handle.None)
        {
            return;
        }

        pressArgs.Pointer.Capture(this);
        DragHandleTo(pressArgs.GetPosition(this));
        pressArgs.Handled = true;
    }

    // Ends a drag.
    protected override void OnPointerReleased(PointerReleasedEventArgs releaseArgs)
    {
        base.OnPointerReleased(releaseArgs);
        _draggedHandle = Handle.None;
        releaseArgs.Pointer.Capture(null);
    }

    // Where a time from key-on lands across the plot.
    private double XOfSeconds(double seconds)
    {
        return EnvelopeTimeAxis.XOfSeconds(seconds, Bounds.Width);
    }

    // Where an envelope level lands up the plot.
    private double YOfLevel(int level)
    {
        return EnvelopeCurve.YOfLevel(level, Bounds.Height);
    }

    // The open row's envelope.
    private EnvelopeState OpenEnvelope()
    {
        return _rows[_openRowIndex!.Value].Registers;
    }

    // The rows with the open row's envelope replaced.
    private EnvelopeChange[] RowsWithOpenEnvelope(EnvelopeState envelope)
    {
        int openRowIndex = _openRowIndex!.Value;
        return [.. _rows.Select((row, rowIndex) => rowIndex == openRowIndex ? row with { Registers = envelope } : row)];
    }

    // Where a handle sits on the open row's stretch of a traced curve, which is the corner where the phase it ends does, or null where the row has no such corner.
    private Point? HandlePosition(EnvelopeTrace trace, Handle handle)
    {
        int openRowIndex = _openRowIndex!.Value;
        var corner = handle switch
        {
            Handle.Attack => trace.CornerEntering(EnvelopePhase.Decay, openRowIndex),
            Handle.Decay => trace.CornerEntering(EnvelopePhase.Sustain, openRowIndex),
            _ => SustainEndOf(trace, openRowIndex),
        };
        if (corner is not { } handleCorner)
        {
            return null;
        }

        return new Point(XOfSeconds(handleCorner.Seconds), YOfLevel(handleCorner.Level));
    }

    // Where a row's sustain fall ends, which is where the row reached silence, or the row's end where it did not, or null where the row never reached
    // its sustain; the last row's end is forever where it settled.
    private EnvelopeCorner? SustainEndOf(EnvelopeTrace trace, int rowIndex)
    {
        var (start, end) = trace.RunOf(rowIndex);
        bool reachesSustain = false;
        for (int cornerIndex = start; cornerIndex < end; cornerIndex++)
        {
            reachesSustain |= trace.Corners[cornerIndex].Phase == EnvelopePhase.Sustain;
        }

        if (!reachesSustain)
        {
            return null;
        }

        var lastOfRow = trace.Corners[end - 1];
        if (end < trace.Corners.Count)
        {
            return lastOfRow.Level == 0 ? lastOfRow : trace.Corners[end];
        }

        return trace.IsSettled ? lastOfRow with { Seconds = double.PositiveInfinity } : lastOfRow;
    }

    // The handle within grabbing distance of the pointer, the nearest where several are, or none.
    private Handle HandleNearest(Point pointer)
    {
        var nearestHandle = Handle.None;
        double nearestDistance = GrabRadius;
        foreach (var handle in HandlesInHitTestOrder)
        {
            if (HandlePosition(_trace!.Value, handle) is not { } position)
            {
                continue;
            }

            double distance = Point.Distance(position, pointer);
            if (distance > nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            nearestHandle = handle;
        }

        return nearestHandle;
    }

    // Moves the dragged handle to the register values nearest the pointer, redraws, and reports the change where anything moved.
    private void DragHandleTo(Point pointer)
    {
        var envelope = OpenEnvelope();
        var moved = MovedByDrag(envelope, pointer);
        if (moved == envelope)
        {
            return;
        }

        _rows = RowsWithOpenEnvelope(moved);
        _trace = SnesEnvelope.TraceThroughChanges(_rows);
        InvalidateVisual();
        Edited?.Invoke(_draggedHandle, DraggedRegistersOf(moved));
    }

    // The envelope with the dragged handle's registers set to whichever values put it nearest the pointer.
    private EnvelopeState MovedByDrag(EnvelopeState envelope, Point pointer)
    {
        Func<Point, double> horizontalDistanceToPointer = position => Math.Abs(position.X - pointer.X);
        var moved = envelope;
        if (_draggedHandle == Handle.Decay)
        {
            moved = SnappedToNearest(moved, EnvelopeState.MaxSustainLevel, Handle.Decay, (probe, value) => probe.WithSustainLevel(value), position => Math.Abs(position.Y - pointer.Y));
        }

        return _draggedHandle switch
        {
            Handle.Attack => SnappedToNearest(moved, EnvelopeState.MaxAttackRate, Handle.Attack, (probe, value) => probe.WithAttackRate(value), horizontalDistanceToPointer),
            Handle.Decay => SnappedToNearest(moved, EnvelopeState.MaxDecayRate, Handle.Decay, (probe, value) => probe.WithDecayRate(value), horizontalDistanceToPointer),
            _ => SnappedToNearest(moved, EnvelopeState.MaxSustainRate, Handle.Sustain, (probe, value) => probe.WithSustainRate(value), horizontalDistanceToPointer),
        };
    }

    // The change that copies the registers the dragged handle moves out of a moved envelope into any other, leaving the rest of it alone.
    private Func<EnvelopeState, EnvelopeState> DraggedRegistersOf(EnvelopeState moved)
    {
        return _draggedHandle switch
        {
            Handle.Attack => state => state.WithAttackRate(moved.AttackRate),
            Handle.Decay => state => state.WithSustainLevel(moved.SustainLevel).WithDecayRate(moved.DecayRate),
            _ => state => state.WithSustainRate(moved.SustainRate),
        };
    }

    // The envelope with one register set to whichever of its values puts a handle nearest the pointer, each candidate measured on the whole curve it would make.
    private EnvelopeState SnappedToNearest(EnvelopeState from, int maxValue, Handle handle, Func<EnvelopeState, int, EnvelopeState> withValue, Func<Point, double> distanceToPointer)
    {
        var placedCandidates = Enumerable.Range(0, maxValue + 1)
            .Select(value => (Value: value, Position: HandlePosition(SnesEnvelope.TraceThroughChanges(RowsWithOpenEnvelope(withValue(from, value))), handle)))
            .Where(candidate => candidate.Position is not null)
            .ToList();
        int nearestValue = placedCandidates.Count == 0 ? 0 : placedCandidates.MinBy(candidate => distanceToPointer(candidate.Position!.Value)).Value;
        return withValue(from, nearestValue);
    }

    // Draws a line down the plot at each marked time but the first.
    private void DrawTimeGrid(DrawingContext context)
    {
        foreach (var (seconds, _) in EnvelopeTimeAxis.MarkedTimes.Skip(1))
        {
            DrawLineDownPlot(context, DrawnSurface.GridLinePen, XOfSeconds(seconds));
        }
    }

    // Draws a line down the plot where each row after the key-on's takes over.
    private void DrawRowStarts(DrawingContext context)
    {
        foreach (var row in _rows.Skip(OffsetRows.FirstRowIndex + 1))
        {
            DrawLineDownPlot(context, DrawnSurface.BarLinePen, XOfSeconds(row.Seconds));
        }
    }

    // Draws a line from the top of the plot to its foot at a position across it.
    private void DrawLineDownPlot(DrawingContext context, IPen pen, double x)
    {
        context.DrawLine(pen, new Point(x, 0), new Point(x, PlotBottom));
    }

    // Draws a line across the open row's stretch at the level its decay stops at.
    private void DrawSustainLine(DrawingContext context)
    {
        int openRowIndex = _openRowIndex!.Value;
        double y = YOfLevel(((OpenEnvelope().SustainLevel + 1) << SustainLevelShift) - 1);
        double rowEndX = openRowIndex + 1 < _rows.Count ? XOfSeconds(_rows[openRowIndex + 1].Seconds) : PlotRight;
        context.DrawLine(DrawnSurface.BarLinePen, new Point(XOfSeconds(_rows[openRowIndex].Seconds), y), new Point(rowEndX, y));
    }

    // Draws the open row's three handles where it has them.
    private void DrawHandles(DrawingContext context, EnvelopeTrace trace)
    {
        foreach (var handle in HandlesInHitTestOrder)
        {
            if (HandlePosition(trace, handle) is { } position)
            {
                context.DrawEllipse(_handleFill, _handleEdgePen, position, HandleRadius, HandleRadius);
            }
        }
    }

    // Which handle, and so which register, a drag is moving.
    private enum Handle
    {
        None,
        Attack,
        Decay,
        Sustain,
    }
}
