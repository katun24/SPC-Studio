using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// A lane of units, where what sits on the lane is a stretch of the song rather than a value at a tick: a unit is placed, selected, dragged, resized,
// copied and deleted exactly as a note on the roll is.
public abstract class UnitLane<TUnit> : ChannelChangeLane, IUnitLane, IShiftResizeSurface
    where TUnit : struct, ILaneUnit<TUnit>
{
    private readonly List<int> _selectedStartTicks = [];
    private readonly List<TUnit> _unitsAtPress = [];
    private readonly List<TUnit> _dragBaselines = [];
    private readonly List<TUnit> _draggedUnits = [];
    private readonly PlacementPreview<TUnit> _preview = new();
    private readonly ShiftResize<TUnit> _shiftResize;
    private bool _isDragging;
    private double _dragAnchorX;
    private int _resizeStartTick;
    private TUnit _grabbedBaseline;
    private int _grabbedIndex;
    private int? _placedLengthTicks;
    private Brush? _previewFillBrush;
    private Pen? _previewBorderPen;
    private Pen? _cutStripePen;

    // The units cut or copied, as offsets from the earliest of them; one list per unit type.
    private static readonly List<TUnit> ClipboardUnits = [];

    // Raised with one edit to make: the units starting at these ticks taken away, and these units put down.
    public event Action<UnitLane<TUnit>, IReadOnlyList<int>, IReadOnlyList<TUnit>>? EditRequested;

    // Raised where the lane has taken a selection, for the window to drop the roll's.
    public event Action? SelectionTaken;

    // Takes the two brushes the units are drawn in, and lets Shift steer the lane's drag and preview.
    protected UnitLane(LaneCellBrushes cellBrushes) : base(cellBrushes)
    {
        _shiftResize = new ShiftResize<TUnit>(_preview, this);
    }

    // The units, which is what this lane holds instead of changes.
    protected abstract IReadOnlyList<TUnit> Units { get; }

    // Whether any unit is selected, which is what Delete and copy act on.
    public bool HasSelection
    {
        get
        {
            return _selectedStartTicks.Count > 0;
        }
    }

    // Whether this lane is the one a paste should read.
    public bool HasClipboard
    {
        get
        {
            return ReferenceEquals(LaneClipboard.Owner, this) && ClipboardUnits.Count > 0;
        }
    }

    // False: what sits on the lane is units.
    public override bool IsDrawnAsRegions
    {
        get
        {
            return false;
        }
    }

    // The lane's default.
    public override int BaseValue
    {
        get
        {
            return DefaultValue;
        }
    }

    // The bottom of the lane's range.
    public override int DefaultValue
    {
        get
        {
            return ValueRange.Min;
        }
    }

    // Whether a drag of units is running.
    protected override bool IsDragging
    {
        get
        {
            return _isDragging;
        }
    }

    // Whether a drag of units is running, which Shift switches between moving and resizing.
    bool IShiftResizeSurface.IsDragRunning
    {
        get
        {
            return _isDragging;
        }
    }

    // The length a fresh unit is placed at: the last one a resize or a pick left behind, or one snap step.
    private int PlacedLengthTicks
    {
        get
        {
            return _placedLengthTicks ?? Song!.SnapTicks;
        }
    }

    // The units as they are drawn: the song's own, with the dragged ones where the drag has carried them while a drag runs.
    private IEnumerable<TUnit> DrawnUnits
    {
        get
        {
            if (!_isDragging)
            {
                return Units;
            }

            return Units.Where(unit => !_unitsAtPress.Contains(unit)).Concat(_draggedUnits).OrderBy(unit => unit.StartTick);
        }
    }

    // What a press on empty lane puts down, over the stretch the press chose.
    protected abstract TUnit NewUnit(int startTick, int lengthTicks);

    // Nothing sits at a tick here: a unit covers a stretch.
    public override int? ChangeValueAt(int tick)
    {
        return null;
    }

    // Drops the unit selection, which selecting notes does.
    public void ClearUnitSelection()
    {
        if (_selectedStartTicks.Count == 0)
        {
            return;
        }

        DropSelection();
        InvalidateVisual();
    }

    // Drops the selection, the preview and this lane's clipboard.
    protected override void OnSongReplaced()
    {
        DropSelection();
        _preview.Hide();

        if (ReferenceEquals(LaneClipboard.Owner, this))
        {
            ClipboardUnits.Clear();
            LaneClipboard.Clear();
        }
    }

    // Answers a press: left on a unit selects it and takes hold of it, left on empty lane places a unit and takes hold of that, and right takes one unit away or drops the selection.
    protected override void HandlePressInLane(PointerPressedEventArgs e, double x, PointerPointProperties buttons)
    {
        if (Song is null)
        {
            return;
        }

        // The units a Shift resize of the preview left showing, which a press puts down as they are, or null.
        List<TUnit>? previewedUnits = _preview.ResizedMembers();
        HidePreview();

        if (buttons.IsRightButtonPressed)
        {
            PressRightButton(x);
            return;
        }

        if (!buttons.IsLeftButtonPressed)
        {
            return;
        }

        int tick = RollMetrics.TickAtX(x);
        if (tick < 0)
        {
            return;
        }

        if (UnitAt(tick) is not TUnit unit)
        {
            Deselect();
            PlaceUnitsAndTakeHold(previewedUnits ?? UnitsToPlace(tick), x, e);
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Deselect();
            if (ToggleInSelection(unit.StartTick))
            {
                _placedLengthTicks = unit.LengthTicks;
            }

            return;
        }

        if (!_selectedStartTicks.Contains(unit.StartTick))
        {
            SelectOnly([unit.StartTick]);
        }

        _placedLengthTicks = unit.LengthTicks;
        ChosenUnit(unit);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            BeginUnitResize(unit, x, e);
        }
        else
        {
            BeginUnitMove(unit, x, e);
        }
    }

    // Takes away the unit under a right press, or drops the selection where the press is on empty lane.
    private void PressRightButton(double x)
    {
        int tick = RollMetrics.TickAtX(x);
        if (tick >= 0 && UnitAt(tick) is TUnit hit)
        {
            RemoveUnit(hit);
            return;
        }

        Deselect();
        ClearUnitSelection();
    }

    // Puts a group of units down, selects them, opens the first where its units have something to edit, and takes hold of it for a move.
    private void PlaceUnitsAndTakeHold(List<TUnit> group, double x, PointerPressedEventArgs e)
    {
        SelectOnly([.. group.Select(placed => placed.StartTick)]);
        EditRequested?.Invoke(this, [], group);
        ChosenUnit(group[0]);
        BeginUnitMove(group[0], x, e);
    }

    // Told which unit a left press landed on or placed; the base deselects, a lane whose units have something to edit selecting it instead.
    protected virtual void ChosenUnit(TUnit unit)
    {
        Deselect();
    }

    // The unit covering a tick, or null where none does.
    private TUnit? UnitAt(int tick)
    {
        return LaneUnits.UnitCovering(Units, tick);
    }

    // Empties the selection, and with it the one length a Shift resize gave the units it laid down again.
    private void DropSelection()
    {
        _selectedStartTicks.Clear();
        _preview.ForgetUniformLength();
    }

    // Selects exactly the units starting at these ticks.
    private void SelectOnly(IReadOnlyList<int> startTicks)
    {
        if (!_selectedStartTicks.SequenceEqual(startTicks))
        {
            _preview.ForgetUniformLength();
        }

        _selectedStartTicks.Clear();
        _selectedStartTicks.AddRange(startTicks);
        ShowSelectionChanged();
    }

    // Adds the unit starting at a tick to the selection or takes it out, and answers whether it was added.
    private bool ToggleInSelection(int startTick)
    {
        bool wasAdded = !_selectedStartTicks.Remove(startTick);
        if (wasAdded)
        {
            _selectedStartTicks.Add(startTick);
        }

        _preview.ForgetUniformLength();
        ShowSelectionChanged();
        return wasAdded;
    }

    // Redraws a changed selection and tells the window where the lane now holds one, for it to drop the roll's.
    private void ShowSelectionChanged()
    {
        InvalidateVisual();
        if (_selectedStartTicks.Count > 0)
        {
            SelectionTaken?.Invoke();
        }
    }

    // Takes hold of the selection to move it, measured from where the press went down.
    private void BeginUnitMove(TUnit grabbedUnit, double x, PointerPressedEventArgs e)
    {
        _shiftResize.IsDragResizing = false;
        TakeHoldOfSelection(grabbedUnit, x, e);
    }

    // Takes hold of the selection to resize it, which applies to the press itself.
    private void BeginUnitResize(TUnit grabbedUnit, double x, PointerPressedEventArgs e)
    {
        _shiftResize.IsDragResizing = true;
        TakeHoldOfSelection(grabbedUnit, x, e);
        MoveDragTo(x);
    }

    // Starts a drag with every selected unit as it stands now as its baseline, and captures the pointer.
    private void TakeHoldOfSelection(TUnit grabbedUnit, double x, PointerPressedEventArgs e)
    {
        _isDragging = true;
        _dragAnchorX = x;
        _grabbedBaseline = grabbedUnit;
        _resizeStartTick = grabbedUnit.StartTick;

        _unitsAtPress.Clear();
        _unitsAtPress.AddRange(SelectedUnits());
        if (_unitsAtPress.Count == 0)
        {
            _unitsAtPress.Add(grabbedUnit);
        }

        _grabbedIndex = Math.Max(0, _unitsAtPress.FindIndex(member => member.StartTick == grabbedUnit.StartTick));

        _dragBaselines.Clear();
        _dragBaselines.AddRange(_unitsAtPress);
        _draggedUnits.Clear();
        _draggedUnits.AddRange(_unitsAtPress);

        e.Pointer.Capture(this);
    }

    // Tells a drag in progress, or the preview under the cursor, whether Shift is down, switching a drag between moving and resizing.
    public void SetResizeModifier(bool isShiftHeld)
    {
        _shiftResize.SetModifier(isShiftHeld);
    }

    // Turns the resize off until Shift is let go.
    public void SuppressResize()
    {
        _shiftResize.Suppress();
    }

    // Makes the units the drag has carried so far its baseline, the grabbed one still the one the cursor drives.
    void IShiftResizeSurface.RebaseDrag()
    {
        Rebase(_draggedUnits, _grabbedIndex);
    }

    // Carries the drag to where the cursor now is.
    void IShiftResizeSurface.FollowCursorWithDrag()
    {
        FollowCursor();
    }

    // Puts the preview back under the cursor.
    void IShiftResizeSurface.ShowPreviewAtCursor()
    {
        ShowPreview(HoverX);
    }

    // Makes the preview the resize's baseline, its first unit the one the cursor drives.
    void IShiftResizeSurface.RebasePreviewResize()
    {
        Rebase(_preview.Members, 0);
    }

    // Makes these units the drag's baseline, measured from where the cursor now is, the one at the index being the one the cursor drives.
    private void Rebase(IReadOnlyList<TUnit> group, int grabbedIndex)
    {
        var baselines = group.ToList();
        _dragBaselines.Clear();
        _dragBaselines.AddRange(baselines);

        _grabbedBaseline = _dragBaselines[grabbedIndex];
        _resizeStartTick = _grabbedBaseline.StartTick;

        if (CursorInLane() is Point cursor)
        {
            _dragAnchorX = cursor.X;
        }
    }

    // Follows the pointer: carries a drag, resizes the preview, or moves the preview under the cursor.
    protected override void HandleMoveInLane(PointerEventArgs e)
    {
        SetResizeModifier(e.KeyModifiers.HasFlag(KeyModifiers.Shift));

        double x = e.GetPosition(this).X;
        if (_isDragging)
        {
            MoveDragTo(x);
            return;
        }

        PreviewAt(x);
    }

    // Takes the preview away when the cursor leaves the lane.
    protected override void OnHoverEnded()
    {
        HidePreview();
    }

    // Takes the preview away, redrawing where one was showing.
    private void HidePreview()
    {
        if (_preview.Hide())
        {
            InvalidateVisual();
        }
    }

    // Re-runs the cursor over content a scroll or a zoom has moved under it: a drag follows it, and otherwise the preview is put under it.
    public override void ContentMoved()
    {
        if (FollowCursor())
        {
            return;
        }

        if (IsPanning || HoverX is not double x)
        {
            return;
        }

        PreviewAt(x);
    }

    // Resizes the preview to a position while Shift is resizing it, and otherwise moves it under the position.
    private void PreviewAt(double x)
    {
        if (_preview.IsResizing)
        {
            UpdatePreviewResize(x);
            return;
        }

        ShowPreview(x);
    }

    // Draws what a left press at a position would put down, or nothing where a press would not place.
    private void ShowPreview(double? x)
    {
        List<TUnit> previewedUnits = [];
        if (Song is not null && x is double position && position >= 0)
        {
            int tick = RollMetrics.TickAtX(position);
            if (UnitAt(tick) is null)
            {
                previewedUnits = UnitsToPlace(tick);
            }
        }

        if (_preview.Show(previewedUnits))
        {
            InvalidateVisual();
        }
    }

    // The units a press on empty lane would put down: one fresh unit with nothing selected, or the selection laid down again with its earliest under the cursor.
    private List<TUnit> UnitsToPlace(int tick)
    {
        var selection = SelectedUnits();
        if (selection.Count == 0)
        {
            return [NewUnit(Song!.SnapDown(tick), PlacedLengthTicks)];
        }

        selection.Sort((first, second) => first.StartTick.CompareTo(second.StartTick));

        int earliestTick = selection[0].StartTick;
        int shift = SnapSpan.OffsetToCursor(Song!, tick, earliestTick, earliestTick);
        int? uniformLength = _preview.UniformLength(PlacedLengthTicks);

        return [.. selection.Select(unit => unit.WithSpan(unit.StartTick + shift, uniformLength ?? unit.LengthTicks))];
    }

    // Resizes the preview to a position, which Shift held over the lane does with no button down.
    private void UpdatePreviewResize(double x)
    {
        if (_preview.Count == 0)
        {
            return;
        }

        var (startDelta, lengthDelta) = ResizeDeltas(x);
        TUnit sized = WithDeltas(_grabbedBaseline, startDelta, lengthDelta);

        var resized = new List<TUnit>(_dragBaselines.Count);
        foreach (var baseline in _dragBaselines)
        {
            resized.Add(baseline.WithSpan(baseline.StartTick + startDelta, sized.LengthTicks));
        }

        if (!_preview.ShowResized(resized))
        {
            return;
        }

        _placedLengthTicks = sized.LengthTicks;
        InvalidateVisual();
    }

    // Carries the drag to a horizontal position, every member of the selection taking the same two deltas read off the grabbed unit.
    protected override void MoveDragTo(double x)
    {
        if (Song is null)
        {
            return;
        }

        RaiseDragAt(x);

        var (startDelta, lengthDelta) = _shiftResize.IsDragResizing ? ResizeDeltas(x) : MoveDeltas(x);

        var dragged = new List<TUnit>(_dragBaselines.Count);
        foreach (var baseline in _dragBaselines)
        {
            dragged.Add(WithDeltas(baseline, startDelta, lengthDelta));
        }

        if (dragged.SequenceEqual(_draggedUnits))
        {
            return;
        }

        _draggedUnits.Clear();
        _draggedUnits.AddRange(dragged);
        RaiseDragMoved(_draggedUnits[_grabbedIndex].StartTick);
        ReachDragTo(_draggedUnits.Max(unit => unit.EndTick()));
        InvalidateVisual();
    }

    // How far a move to a position carries the grabbed unit's start, in whole snap steps from the step the press went down in.
    private (int StartDelta, int LengthDelta) MoveDeltas(double x)
    {
        int startDelta = RollMetrics.SnapStepsMovedTicks(Song!, _dragAnchorX, x);
        return (HeldOffSongStart(startDelta), 0);
    }

    // How far a resize to a position moves the grabbed unit's start and how much longer it makes it.
    private (int StartDelta, int LengthDelta) ResizeDeltas(double x)
    {
        var (startTick, lengthTicks) = SnapSpan.ResizedToTick(Song!, RollMetrics.TickAtX(x), ref _resizeStartTick, _grabbedBaseline.LengthTicks);
        int startDelta = startTick - _grabbedBaseline.StartTick;
        return (HeldOffSongStart(startDelta), lengthTicks - _grabbedBaseline.LengthTicks);
    }

    // A start delta held so that no member of the group is carried off the front of the song.
    private int HeldOffSongStart(int startDelta)
    {
        int earliestTick = _dragBaselines.Select(baseline => baseline.StartTick).DefaultIfEmpty(int.MaxValue).Min();
        return GroupStep.HeldOffSongStart(startDelta, earliestTick);
    }

    // One member of the group with the drag's deltas applied, never shortened below one snap step or its own length.
    private TUnit WithDeltas(TUnit baseline, int startDelta, int lengthDelta)
    {
        return baseline.WithSpan(baseline.StartTick + startDelta, SnapSpan.Lengthened(Song!, baseline.LengthTicks, lengthDelta));
    }

    // Drops the units where the drag left them, the ones it started from taken away and the ones it ended on put down as one edit.
    protected override void EndDrag()
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        _shiftResize.IsDragResizing = false;
        RaiseDragEnded();

        if (_draggedUnits[_grabbedIndex].LengthTicks != _unitsAtPress[_grabbedIndex].LengthTicks)
        {
            _placedLengthTicks = _draggedUnits[_grabbedIndex].LengthTicks;
        }

        if (!_draggedUnits.SequenceEqual(_unitsAtPress))
        {
            DropDraggedUnits();
        }

        RaiseDragMoved(null);
        ReleaseDragReach();
        InvalidateVisual();
    }

    // Asks for the drag's edit, selects the units where they landed, and moves the editor onto the grabbed one.
    private void DropDraggedUnits()
    {
        var removedStartTicks = _unitsAtPress.Select(unit => unit.StartTick).ToList();
        var placedUnits = _draggedUnits.ToList();

        SelectOnly([.. placedUnits.Select(unit => unit.StartTick)]);
        EditRequested?.Invoke(this, removedStartTicks, placedUnits);

        int grabbedFromTick = _unitsAtPress[_grabbedIndex].StartTick;
        int grabbedToTick = placedUnits[_grabbedIndex].StartTick;
        if (SelectedTick == grabbedFromTick && grabbedToTick != grabbedFromTick)
        {
            Select(grabbedToTick);
        }
    }

    // Takes one unit off the song and out of the selection, which is the right button.
    private void RemoveUnit(TUnit unit)
    {
        if (_selectedStartTicks.Remove(unit.StartTick))
        {
            _preview.ForgetUniformLength();
        }

        Deselect();
        InvalidateVisual();
        EditRequested?.Invoke(this, [unit.StartTick], []);
    }

    // Takes the selected units off the song, which is Delete and Backspace.
    public bool DeleteSelected()
    {
        if (_selectedStartTicks.Count == 0)
        {
            return false;
        }

        var removedStartTicks = _selectedStartTicks.ToList();
        ClearUnitSelection();
        Deselect();
        EditRequested?.Invoke(this, removedStartTicks, []);
        return true;
    }

    // Copies the selected units, as offsets from the earliest of them, and takes the clipboard off whatever held it.
    public bool CopySelected()
    {
        var units = SelectedUnits();
        if (units.Count == 0)
        {
            return false;
        }

        int anchorTick = units.Min(unit => unit.StartTick);

        ClipboardUnits.Clear();
        foreach (var unit in units)
        {
            ClipboardUnits.Add(unit.WithSpan(unit.StartTick - anchorTick, unit.LengthTicks));
        }

        LaneClipboard.Owner = this;
        return true;
    }

    // Copies and then deletes the selected units, as one edit.
    public bool CutSelected()
    {
        return CopySelected() && DeleteSelected();
    }

    // Puts the clipboard down at a tick, keeping the spacing it was copied with.
    public bool Paste(int anchorTick)
    {
        if (Song is null || ClipboardUnits.Count == 0)
        {
            return false;
        }

        int startTick = Song.SnapDown(Math.Max(0, anchorTick));
        var placedUnits = ClipboardUnits.Select(unit => unit.WithSpan(startTick + unit.StartTick, unit.LengthTicks)).ToList();

        SelectOnly([.. placedUnits.Select(unit => unit.StartTick)]);
        EditRequested?.Invoke(this, [], placedUnits);
        return true;
    }

    // The selected units themselves, a stale start tick passed over.
    private List<TUnit> SelectedUnits()
    {
        return Units.Where(unit => _selectedStartTicks.Contains(unit.StartTick)).ToList();
    }

    // The middle of the visible part of the unit starting at a tick, along the top of the lane, where the editor stands.
    public override Point CellTopCenter(int tick)
    {
        if (UnitStartingAt(tick) is not TUnit unit)
        {
            return new Point(RollMetrics.XOfTick(tick) + CellWidth / 2, 0);
        }

        var (x, width) = SpanOf(unit);
        return new Point(VisibleCenterX(x, x + width), 0);
    }

    // The unit starting at a tick as it is drawn, one a drag is holding first.
    private TUnit? UnitStartingAt(int tick)
    {
        if (_isDragging && LaneUnits.UnitStartingAt(_draggedUnits, tick) is TUnit dragged)
        {
            return dragged;
        }

        return LaneUnits.UnitStartingAt(Units, tick);
    }

    // Where a unit starts along the lane and how wide it is drawn.
    private static (double X, double Width) SpanOf(TUnit unit)
    {
        return (RollMetrics.XOfTick(unit.StartTick), unit.LengthTicks * RollMetrics.PixelsPerTick);
    }

    // The box a unit is drawn in, the roll's own note box over the unit's stretch.
    private Rect BoxOf(TUnit unit)
    {
        var (x, width) = SpanOf(unit);
        return NoteBlock.BlockRect(x, width, 0, RowHeight);
    }

    // Whether a unit is drawn as selected, which a drag answers from its own set.
    private bool IsDrawnSelected(TUnit unit)
    {
        return _isDragging ? _draggedUnits.Contains(unit) : _selectedStartTicks.Contains(unit.StartTick);
    }

    // Draws the lane, then covers the space past the end of the grid so a press there can place units.
    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        RollMetrics.DrawPointerSpacePastEnd(dc, Song, RowHeight);
    }

    // Draws the units as the roll's note boxes, the cuts a landing group would make, the stretches the notes decide and the placement preview.
    protected override void DrawContent(DrawingContext dc, double left, double right)
    {
        DrawUnits(dc, left, right);
        DrawCuts(dc, left, right);
        DrawOverriddenRegions(dc, left, right);
        DrawPreview(dc, left, right);
    }

    // Draws each unit as a note box in the lane's color, with whatever its lane writes inside it.
    private void DrawUnits(DrawingContext dc, double left, double right)
    {
        foreach (var unit in DrawnUnits)
        {
            var (x, width) = SpanOf(unit);
            if (x > right)
            {
                break;
            }

            if (x + width < left)
            {
                continue;
            }

            bool isSelected = IsDrawnSelected(unit);
            var box = BoxOf(unit);
            dc.DrawRectangle(isSelected ? CellBrushes.Selected : CellBrushes.Cell, isSelected ? SelectedCellBorder : CellBorder, box);
            DrawInside(dc, unit, box);
        }
    }

    // Writes no value over a stretch the notes decide.
    protected override void DrawOverriddenValue(DrawingContext dc, DrawnRegion region, double x0, double x1)
    {
    }

    // Draws the units a press would put down, translucent over the units they would replace.
    private void DrawPreview(DrawingContext dc, double left, double right)
    {
        if (_preview.Count == 0)
        {
            return;
        }

        var color = NoteBlock.ColorOf(CellBrushes.Cell);
        _previewFillBrush ??= NoteBlock.PreviewFill(color);
        _previewBorderPen ??= NoteBlock.PreviewBorderPen(color);

        foreach (var unit in _preview.Members)
        {
            var (x, width) = SpanOf(unit);
            if (x > right || x + width < left)
            {
                continue;
            }

            dc.DrawRectangle(_previewFillBrush, _previewBorderPen, BoxOf(unit));
        }
    }

    // Draws stripes over the part of every unit that the units about to land would cut away.
    private void DrawCuts(DrawingContext dc, double left, double right)
    {
        IReadOnlyList<TUnit> landingUnits = _isDragging ? _draggedUnits : _preview.Members;
        if (landingUnits.Count == 0)
        {
            return;
        }

        // The units already in the song that the landing ones can cut, those a drag holds left out.
        var standingUnits = _isDragging ? Units.Where(unit => !_unitsAtPress.Contains(unit)).ToList() : Units;
        var cuts = LandingCuts.Of(landingUnits, unit => (unit.StartTick, unit.EndTick()), _ => standingUnits, (_, _) => true, left, right);
        foreach (var (unit, cutter) in cuts)
        {
            DrawCut(dc, unit, cutter);
        }
    }

    // Draws the stripes over the stretch of one unit that another covers, in the lane's own cut stripe color.
    private void DrawCut(DrawingContext dc, TUnit unit, TUnit cutter)
    {
        if (NoteBlock.CutRect(unit.StartTick, unit.EndTick(), cutter.StartTick, cutter.EndTick(), RowHeight) is not Rect cut)
        {
            return;
        }

        _cutStripePen ??= NoteBlock.CutStripePenOf(NoteBlock.PendingMarkFill(NoteBlock.ColorOf(CellBrushes.Cell)));
        NoteBlock.DrawCutStripes(dc, new RectangleGeometry(BoxOf(unit)), cut, _cutStripePen);
    }

    // Draws over a unit's own box, for a lane whose units say something; the base draws nothing.
    protected virtual void DrawInside(DrawingContext dc, TUnit unit, Rect box)
    {
    }
}
