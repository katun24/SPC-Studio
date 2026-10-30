using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SpcStudio.Controls;

// The lane's pointer handling: a press selecting a region or inserting, removing or taking hold of a divider, a divider carried along the lane, and the middle-button pan.
public abstract partial class ChangeLane
{
    private const double DividerGrabDistance = 4;

    private bool _isDragging;
    private int _dragFromTick;
    private int _dragToTick;
    private double _dragAnchorX;
    private bool _isPanning;
    private readonly WindowCursor _windowCursor = new();
    private bool _isCursorInside;

    // The tick the cursor is over, or null where the pointer is not over this lane.
    public int? CursorTick
    {
        get
        {
            return HoverX is double x && x >= 0 ? RollMetrics.TickAtX(x) : null;
        }
    }

    // Where the cursor is along the lane, or null where the pointer is not over this lane.
    protected double? HoverX
    {
        get
        {
            return _isCursorInside && CursorInLane() is Point cursor ? cursor.X : null;
        }
    }

    protected bool IsPanning
    {
        get
        {
            return _isPanning;
        }
    }

    // Whether a drag of the lane's own content is running.
    protected virtual bool IsDragging
    {
        get
        {
            return _isDragging;
        }
    }

    // Hands a left or right press to the lane's own content and starts a pan or a ruler line's drag on a middle press.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_song is null || IsDragging || _isPanning)
        {
            return;
        }

        _windowCursor.Remember(this, e);
        var buttons = e.GetCurrentPoint(this).Properties;
        if (!buttons.IsMiddleButtonPressed)
        {
            HandlePressInLane(e, e.GetPosition(this).X, buttons);
            return;
        }

        TimeRuler.AnswerViewWheelPress(e, press => WheelDragRequested?.Invoke(press), BeginPan);
    }

    // What a left or right press means over the lane's own content: left selects a region or takes hold of a divider, right inserts a divider or takes one away.
    protected virtual void HandlePressInLane(PointerPressedEventArgs e, double x, PointerPointProperties buttons)
    {
        int? dividerTick = DividerAt(x);
        if (buttons.IsRightButtonPressed)
        {
            if (dividerTick is int removed)
            {
                RemoveDivider(removed);
                return;
            }

            InsertDivider(x);
            return;
        }

        if (!buttons.IsLeftButtonPressed)
        {
            return;
        }

        if (dividerTick is int held)
        {
            Select(held);
            BeginDrag(held, x, e);
            return;
        }

        Select(RegionStartAt(x));
    }

    // Asks the window for a change at a tick where none sits yet, and selects it.
    private void PlaceIfEmptyAndSelect(int tick)
    {
        if (ChangeValueAt(tick) is null)
        {
            PlaceRequested?.Invoke(this, tick);
        }

        Select(tick);
    }

    // Splits a region at the snap boundary nearest the press, or selects the first region where that boundary is the song's start.
    private void InsertDivider(double x)
    {
        int tick = RollMetrics.SnappedTickAtX(_song!, x);
        if (tick <= 0)
        {
            Select(0);
            return;
        }

        PlaceIfEmptyAndSelect(tick);
    }

    // The divider a position is within grabbing distance of, the nearest where two are, or null.
    private int? DividerAt(double x)
    {
        int? nearestTick = null;
        double nearestDistance = DividerGrabDistance;
        foreach (var change in DrawnChanges)
        {
            if (change.Tick <= 0)
            {
                continue;
            }

            double distance = Math.Abs(x - RollMetrics.XOfTick(change.Tick));
            if (distance > nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            nearestTick = change.Tick;
        }

        return nearestTick;
    }

    // The tick the region covering a position starts at, which is 0 in the first one.
    private int RegionStartAt(double x)
    {
        int tickAtX = RollMetrics.TickAtX(x);
        int start = 0;
        foreach (var change in DrawnChanges)
        {
            if (change.Tick > 0 && change.Tick <= tickAtX)
            {
                start = Math.Max(start, change.Tick);
            }
        }

        return start;
    }

    // Takes hold of a divider for a drag, leaving the song untouched until the drop.
    private void BeginDrag(int tick, double x, PointerPressedEventArgs e)
    {
        _isDragging = true;
        _dragFromTick = tick;
        _dragToTick = tick;
        _dragAnchorX = x;
        e.Pointer.Capture(this);
    }

    // Takes hold of the view for a middle-drag.
    private void BeginPan(PointerPressedEventArgs e)
    {
        _isPanning = true;
        WheelDrag.Follow(this, e, moved => PanRequested?.Invoke(moved));
        OnHoverEnded();
    }

    // Remembers where the cursor is and hands a movement to the lane's own content while no pan is running.
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _windowCursor.Remember(this, e);
        if (_isPanning)
        {
            return;
        }

        HandleMoveInLane(e);
    }

    // What a pointer movement over the lane's own content does, a pan having already been dealt with.
    protected virtual void HandleMoveInLane(PointerEventArgs e)
    {
        if (_isDragging)
        {
            MoveDragTo(e.GetPosition(this).X);
        }
    }

    // Carries the drag to a horizontal position in the lane's own coordinates, growing or shrinking the roll with it.
    protected virtual void MoveDragTo(double x)
    {
        if (_song is null)
        {
            return;
        }

        DragAt?.Invoke(x);
        int toTick = DragTargetTick(x);
        if (toTick == _dragToTick)
        {
            return;
        }

        _dragToTick = toTick;
        ReachDragTo(toTick + 1);

        InvalidateVisual();
        DragMoved?.Invoke(this, toTick);
    }

    // Re-runs the drag from where the cursor is now, and answers whether one is still running.
    public bool FollowCursor()
    {
        return _windowCursor.FollowOver(this, IsDragging, cursor => MoveDragTo(cursor.X));
    }

    // Where the cursor is in the lane's own coordinates, as of the last pointer event.
    protected Point? CursorInLane()
    {
        return _windowCursor.PositionOver(this);
    }

    // Lets a lane drawing something under the cursor take it away when the pointer leaves or a pan takes it.
    protected virtual void OnHoverEnded()
    {
    }

    // Re-runs the cursor over content that has moved under it, for a lane drawing something from the cursor.
    public virtual void ContentMoved()
    {
    }

    // Marks the cursor as over the lane and remembers where it is.
    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _isCursorInside = true;
        _windowCursor.Remember(this, e);
    }

    // Marks the cursor as off the lane and takes away what was drawn under it.
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _isCursorInside = false;
        OnHoverEnded();
    }

    // Lets go of the pointer when the left button comes up during a drag.
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left && IsDragging)
        {
            e.Pointer.Capture(null);
        }
    }

    // Ends the pan and drops the dragged divider once the pointer is let go.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isPanning = false;
        EndDrag();
    }

    // Drops the selection and asks the window to take the divider at a tick off the song.
    private void RemoveDivider(int tick)
    {
        Deselect();
        RemoveRequested?.Invoke(this, tick);
    }

    // Raises the drag position for a lane of another shape.
    protected void RaiseDragAt(double x)
    {
        DragAt?.Invoke(x);
    }

    // Raises the end of a drag for a lane of another shape.
    protected void RaiseDragEnded()
    {
        DragEnded?.Invoke();
    }

    // Raises where a drag has carried the selection for a lane of another shape.
    protected void RaiseDragMoved(int? tick)
    {
        DragMoved?.Invoke(this, tick);
    }

    // Lets the roll follow a drag out to the first tick past what it covers, telling the window where that changed how long the roll is.
    protected void ReachDragTo(int reachTick)
    {
        int rollTicksBefore = _song!.RollLengthTicks;
        _song.SetLiftedReach(reachTick);
        RollResizes.RaiseIfChanged(_song, rollTicksBefore, RollResized);
    }

    // Lets the roll go back to the length the song alone gives it once a drag has dropped, and tells the window.
    protected void ReleaseDragReach()
    {
        _song?.SetLiftedReach(null);
        RollResized?.Invoke();
    }

    // Drops the divider where the drag left it and puts the selection and the editor over it.
    protected virtual void EndDrag()
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        DragEnded?.Invoke();
        int fromTick = _dragFromTick;
        int toTick = _dragToTick;
        if (toTick != fromTick)
        {
            MoveRequested?.Invoke(this, fromTick, toTick);
        }

        ReleaseDragReach();
        DragMoved?.Invoke(this, null);
        if (toTick != fromTick)
        {
            Select(toTick);
        }

        InvalidateVisual();
    }

    // Where the drag has carried the divider: whole snap steps from the step the press went down in, walled in by the dividers either side.
    private int DragTargetTick(double x)
    {
        int toTick = _dragFromTick + RollMetrics.SnapStepsMovedTicks(_song!, _dragAnchorX, x);
        var (firstTick, lastTick) = RegionDragRange(_song!.SnapTicks);
        return lastTick is int ceiling ? Math.Clamp(toTick, firstTick, ceiling) : Math.Max(toTick, firstTick);
    }

    // The ticks the divider being dragged may sit between, keeping its offset inside the snap step; no last tick for the last divider.
    private (int FirstTick, int? LastTick) RegionDragRange(int snapTicks)
    {
        int offset = _dragFromTick % snapTicks;
        int previousDivider = 0;
        int? nextDivider = null;
        foreach (var change in Changes)
        {
            if (change.Tick <= 0 || change.Tick == _dragFromTick)
            {
                continue;
            }

            if (change.Tick < _dragFromTick)
            {
                previousDivider = Math.Max(previousDivider, change.Tick);
            }
            else if (nextDivider is not int already || change.Tick < already)
            {
                nextDivider = change.Tick;
            }
        }

        int? lastTick = nextDivider is int wall ? LastStepBefore(wall, offset, snapTicks) : null;
        return (FirstStepPast(previousDivider, offset, snapTicks), lastTick);
    }

    // The first tick past a wall that keeps a given offset inside the snap step.
    private static int FirstStepPast(int wallTick, int offset, int snapTicks)
    {
        int tick = offset + ((int)Math.Floor((wallTick - offset) / (double)snapTicks) + 1) * snapTicks;
        return tick > wallTick ? tick : tick + snapTicks;
    }

    // The last tick before a wall that keeps a given offset inside the snap step.
    private static int LastStepBefore(int wallTick, int offset, int snapTicks)
    {
        int tick = offset + ((int)Math.Ceiling((wallTick - offset) / (double)snapTicks) - 1) * snapTicks;
        return tick < wallTick ? tick : tick - snapTicks;
    }

}
