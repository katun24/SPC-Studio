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
    private MouseButton _dragButton;
    private int? _previewedDividerTick;
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
    public virtual bool IsDragging
    {
        get
        {
            return _isDragging;
        }
    }

    // Takes the focus, then hands a left or right press to the lane's own content and starts a pan or a ruler line's drag on a middle press.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
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

    // What a left or right press means over the lane's own content: left selects a region or takes hold of a divider, right inserts a divider and takes
    // hold of it or takes one away.
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

            InsertDividerAndTakeHold(x, e);
            return;
        }

        if (!buttons.IsLeftButtonPressed)
        {
            return;
        }

        if (dividerTick is int held)
        {
            Deselect();
            BeginDrag(held, x, e, MouseButton.Left);
            return;
        }

        Select(RegionStartAt(x));
    }

    // Splits a region at the snap boundary nearest the press and takes hold of the divider for as long as the right button is held, or selects the first
    // region where that boundary is the song's start.
    private void InsertDividerAndTakeHold(double x, PointerPressedEventArgs e)
    {
        if (DividerSlotAt(x) is not int tick)
        {
            Select(0);
            return;
        }

        Deselect();
        if (ChangeValueAt(tick) is null)
        {
            PlaceRequested?.Invoke(this, tick);
        }

        if (ChangeValueAt(tick) is not null)
        {
            BeginDrag(tick, x, e, MouseButton.Right);
        }
    }

    // The snap boundary nearest a position, where a right press puts a divider, or null where that boundary is the song's start.
    private int? DividerSlotAt(double x)
    {
        int tick = RollMetrics.SnappedTickAtX(_song!, x);
        return tick > 0 ? tick : null;
    }

    // Where a right press at a position would put a new divider, or null where it would take one away, take hold of one or place none.
    private int? NewDividerTickAt(double x)
    {
        if (DividerAt(x) is not null || DividerSlotAt(x) is not int tick || tick >= GridEndTick || ChangeValueAt(tick) is not null)
        {
            return null;
        }

        return tick;
    }

    // The divider a position is within grabbing distance of, the nearest where two are, or null.
    private int? DividerAt(double x)
    {
        var dividerTicks = Changes.Select(change => change.Tick).Where(tick => tick > 0);
        return GrabDistance.Nearest(dividerTicks, tick => Math.Abs(x - RollMetrics.XOfTick(tick)), DividerGrabDistance);
    }

    // The tick the region covering a position starts at, which is 0 in the first one.
    private int RegionStartAt(double x)
    {
        int tickAtX = RollMetrics.TickAtX(x);
        int start = 0;
        foreach (var change in Changes)
        {
            if (change.Tick > 0 && change.Tick <= tickAtX)
            {
                start = Math.Max(start, change.Tick);
            }
        }

        return start;
    }

    // Takes hold of a divider for a drag held by one button.
    private void BeginDrag(int tick, double x, PointerPressedEventArgs e, MouseButton heldButton)
    {
        _isDragging = true;
        _dragFromTick = tick;
        _dragToTick = tick;
        _dragAnchorX = x;
        _dragButton = heldButton;
        HideDividerPreview();
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

    // What a pointer movement over the lane's own content does, a pan having already been dealt with: a drag follows it, and otherwise the preview is
    // put under it.
    protected virtual void HandleMoveInLane(PointerEventArgs e)
    {
        double x = e.GetPosition(this).X;
        if (IsDragging)
        {
            MoveDragTo(x);
            return;
        }

        PreviewAt(x);
    }

    // Draws what a press at a position would put down: a dimmed divider where a right press would put one, or none where it would not.
    protected virtual void PreviewAt(double x)
    {
        int? tick = _song is null ? null : NewDividerTickAt(x);
        if (tick == _previewedDividerTick)
        {
            return;
        }

        _previewedDividerTick = tick;
        InvalidateVisual();
    }

    // Takes the divider preview away, redrawing where one was showing.
    private void HideDividerPreview()
    {
        if (_previewedDividerTick is null)
        {
            return;
        }

        _previewedDividerTick = null;
        InvalidateVisual();
    }

    // Carries the drag to a horizontal position in the lane's own coordinates, asking the window to move the divider there and growing or shrinking
    // the roll with it.
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

        int fromTick = _dragToTick;
        _dragToTick = toTick;
        ReachDragTo(toTick + 1);
        MoveRequested?.Invoke(this, fromTick, toTick);
        InvalidateVisual();
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

    // Takes away what is drawn under the cursor when the pointer leaves or a pan takes it.
    protected virtual void OnHoverEnded()
    {
        HideDividerPreview();
    }

    // Re-runs the cursor over content a scroll or a zoom has moved under it: a drag follows it, and otherwise the preview is put under it.
    public void ContentMoved()
    {
        if (FollowCursor() || _isPanning || HoverX is not double x)
        {
            return;
        }

        PreviewAt(x);
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

    // Lets go of the pointer when the button holding a drag comes up: the one that took hold of a divider, and the left one for any other drag.
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (IsDragging && e.InitialPressMouseButton == (_isDragging ? _dragButton : MouseButton.Left))
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

    // Lets go of the divider where the drag left it.
    protected virtual void EndDrag()
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        DragEnded?.Invoke();
        ReleaseDragReach();
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
            if (change.Tick <= 0 || change.Tick == _dragToTick)
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
