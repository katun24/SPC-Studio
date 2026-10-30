using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The roll's pointer handling: a press placing, picking, selecting, removing or taking hold of a note, the middle-button pan, the audition a press
// holds, and the cursor followed through a scroll or a zoom.
public sealed partial class PianoRoll
{
    private readonly WindowCursor _windowCursor = new();
    private bool _hasPressMoved;
    private bool _isCursorInside;
    private bool _isPanning;
    private bool _isAuditioning;
    private Point _cursorInGrid;

    // The note a plain press went down on, which the release selects where the cursor never moved.
    private Note? _pressedNote;

    // Whether the middle button is panning the roll.
    public bool IsPanning
    {
        get
        {
            return _isPanning;
        }
    }

    // The tick the cursor is over, or null where the pointer is not over the grid.
    public int? CursorTick
    {
        get
        {
            return _isCursorInside ? Math.Max(0, RollMetrics.TickAtX(_cursorInGrid.X)) : null;
        }
    }

    // Whether a gesture holding the pointer's capture is running.
    private bool IsGestureRunning
    {
        get
        {
            return IsDraggingNotes || _isAuditioning || _isPanning || _isSweepHeld;
        }
    }

    // Takes the focus and answers a press by its button, unless a gesture already holds the capture.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (IsGestureRunning)
        {
            return;
        }

        _isCursorInside = true;
        Point cursor = e.GetPosition(this);
        var buttons = e.GetCurrentPoint(this).Properties;
        _windowCursor.Remember(this, e);
        if (buttons.IsMiddleButtonPressed)
        {
            TimeRuler.AnswerViewWheelPress(e, press => WheelDragRequested?.Invoke(press), BeginPan);
            return;
        }

        if (buttons.IsRightButtonPressed)
        {
            PressRightButton(cursor, e);
            return;
        }

        PressLeftButton(cursor, e);
    }

    // Answers a right press: the bend joint under it is taken off its note, or else the note under it is taken away, and a selection rectangle is swept out where there is neither.
    private void PressRightButton(Point cursor, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt) && BendNodeAt(cursor) is (Note owner, int nodeIndex))
        {
            if (RemoveBendNode(owner, nodeIndex))
            {
                SongEdited?.Invoke();
            }

            return;
        }

        var noteUnderCursor = e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? AnyChannelNoteAt(cursor) : EditedChannelNoteAt(cursor);
        if (noteUnderCursor is (int channel, Note note) && RemoveNote(channel, note))
        {
            SongEdited?.Invoke();
            return;
        }

        BeginSweep(cursor, e);
    }

    // Answers a left press on the grid: a bend joint put on a note with B, a pick with Alt, a bend joint taken hold of, a note selected and taken hold of, or notes placed on empty grid.
    private void PressLeftButton(Point cursor, PointerPressedEventArgs e)
    {
        if (_isNodeInsertKeyHeld)
        {
            InsertBendNodeAndTakeHold(cursor, e);
            return;
        }

        if (!TryGetCellAt(cursor, out int tick, out int semitone))
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            PickNoteOnAnyChannel(cursor, tick, semitone, e);
            return;
        }

        bool isSelectingOrResizing = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!isSelectingOrResizing && BendNodeAt(cursor) is (Note owner, int jointIndex))
        {
            BeginBendNodeDrag(owner, owner, jointIndex, cursor, e);
            return;
        }

        if (NoteDrawnAt(_selectedChannel, cursor, tick, semitone) is not Note pressed)
        {
            PlaceAndTakeHold(cursor, tick, semitone, e);
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            TogglePressedNote(pressed, cursor, e);
        }
        else
        {
            _pressedNote = pressed;
            _hasPressMoved = false;
            TakeHoldOfNote(pressed, cursor, e);
        }

        Refresh();
    }

    // Takes a note a Ctrl press went down on into the selection or out of it, taking hold of it where it went in.
    private void TogglePressedNote(Note pressed, Point cursor, PointerPressedEventArgs e)
    {
        if (ToggleInSelection(_selectedChannel, pressed))
        {
            TakeHoldOfNote(pressed, cursor, e);
        }
    }

    // Makes a pressed note's length the next placed note's, sounds it and starts its drag.
    private void TakeHoldOfNote(Note pressed, Point cursor, PointerPressedEventArgs e)
    {
        _placedNoteLengthTicks = pressed.LengthTicks;
        BeginPressAudition(pressed, e);
        BeginDragOfExistingNote(pressed, cursor, e);
    }

    // Places what a click on an empty cell puts down, reporting a roll it lengthened, sounds and selects it - beside the selection where Ctrl is held -
    // and starts its drag, or reports that there is no instrument to place.
    private void PlaceAndTakeHold(Point cursor, int tick, int semitone, PointerPressedEventArgs e)
    {
        var group = PlacementsForClick(tick, semitone);
        if (group.Count == 0)
        {
            PlacementRefused?.Invoke();
            return;
        }

        int rollTicksBefore = _song.RollLengthTicks;
        _song.PlaceNotes([.. group.Select(member => member.ChannelAndNote)]);
        ReportRollResize(rollTicksBefore);
        BeginPressAudition(group[0].Note, e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _keysKeptByPlacement.AddRange(_selectedKeys);
        }

        Select(KeysBesideKeptSelection(group.Select(member => member.ChannelAndNote)));
        BeginDragOfPlacedGroup(group, cursor, e);
        Refresh();
    }

    // Finds the tick and the semitone of the cell a point falls in, the space past the end of the grid included, answering false left of the grid or above or below its rows.
    private bool TryGetCellAt(Point point, out int tick, out int semitone)
    {
        tick = RollMetrics.TickAtX(point.X);
        semitone = 0;
        if (point.X < 0 || !RollMetrics.TryGetRowAt(point.Y, out int row))
        {
            return false;
        }

        semitone = RollMetrics.SemitoneOfRow(row);
        return true;
    }

    // Takes hold of the view for a middle-drag.
    private void BeginPan(PointerPressedEventArgs e)
    {
        _isPanning = true;
        WheelDrag.Follow(this, e, moved => PanRequested?.Invoke(moved));
        HidePreview();
    }

    // The edited channel's note drawn under a point, or null.
    private (int Channel, Note Note)? EditedChannelNoteAt(Point cursor)
    {
        if (!TryGetCellAt(cursor, out int tick, out int semitone))
        {
            return null;
        }

        return NoteDrawnAt(_selectedChannel, cursor, tick, semitone) is Note note ? (_selectedChannel, note) : null;
    }

    // The note drawn under a point on the edited channel or, with the other channels showing, on the first other channel holding one, or null.
    private (int Channel, Note Note)? AnyChannelNoteAt(Point cursor)
    {
        if (EditedChannelNoteAt(cursor) is { } edited)
        {
            return edited;
        }

        if (!_isShowingOtherChannels || !TryGetCellAt(cursor, out int tick, out int semitone))
        {
            return null;
        }

        for (int channel = 0; channel < _song.ChannelCount; channel++)
        {
            if (channel != _selectedChannel && NoteDrawnAt(channel, cursor, tick, semitone) is Note other)
            {
                return (channel, other);
            }
        }

        return null;
    }

    // Takes one note off a channel and out of the selection, answering whether it was there.
    private bool RemoveNote(int channel, Note note)
    {
        if (!_song.RemoveNoteAt(channel, note.StartTick))
        {
            return false;
        }

        var key = (channel, note.StartTick);
        if (_selectedKeySet.Contains(key))
        {
            FlipInSelection([key]);
        }

        Refresh();
        return true;
    }

    // Takes one bend node off a note on the edited channel, leaving the note, answering whether the node could go.
    private bool RemoveBendNode(Note note, int nodeIndex)
    {
        if (BendNodes.WithNodeRemoved(note, nodeIndex) is not Note withoutNode)
        {
            return false;
        }

        _song.SetNoteBendPath(_selectedChannel, note.StartTick, withoutNode);
        ShowNoteRewrittenInPlace();
        return true;
    }

    // A channel's note drawn under a point, along its own row or near its own stretch of its bend band, or null: the note sounding at the point's tick
    // first, and then the ones either side whose band reaches past their own ends to the point.
    private Note? NoteDrawnAt(int channel, Point cursor, int tick, int semitone)
    {
        double bandReach = NoteShape.HalfBandHeight(Space.Px2);
        int[] candidateTicks = [tick, RollMetrics.TickAtX(cursor.X + bandReach), RollMetrics.TickAtX(cursor.X - bandReach)];
        foreach (int candidateTick in candidateTicks)
        {
            if (_song.NoteAt(channel, candidateTick) is Note candidate && IsNoteDrawnAt(candidate, cursor, tick, semitone))
            {
                return candidate;
            }
        }

        return null;
    }

    // Whether a note is drawn under a point: on its own row at a tick it sounds on, or near its own stretch of its bend band.
    private static bool IsNoteDrawnAt(Note note, Point cursor, int tick, int semitone)
    {
        if (note.Bends.IsEmpty)
        {
            return note.Semitone == semitone && note.StartTick <= tick && tick < note.EndTick();
        }

        return NoteShape.IsOnNoteOwnBand(note, cursor, Space.Px2);
    }

    // Selects and sounds the note under a point on the first channel after the edited one that holds one, wrapping round to the edited one last.
    private void PickNoteOnAnyChannel(Point cursor, int tick, int semitone, PointerPressedEventArgs e)
    {
        if (!_isShowingOtherChannels)
        {
            TryPickNoteOn(_selectedChannel, cursor, tick, semitone, e);
            return;
        }

        for (int step = 1; step <= _song.ChannelCount; step++)
        {
            int channel = (_selectedChannel + step) % _song.ChannelCount;
            if (TryPickNoteOn(channel, cursor, tick, semitone, e))
            {
                return;
            }
        }
    }

    // Moves the editing to a channel and selects and sounds its note under a point, answering whether it had one.
    private bool TryPickNoteOn(int channel, Point cursor, int tick, int semitone, PointerPressedEventArgs e)
    {
        if (NoteDrawnAt(channel, cursor, tick, semitone) is not Note picked)
        {
            return false;
        }

        if (channel != _selectedChannel)
        {
            ChannelPicked?.Invoke(channel);
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Select(channel, picked);
        }
        else if (!ToggleInSelection(channel, picked))
        {
            Refresh();
            return true;
        }

        _placedNoteLengthTicks = picked.LengthTicks;
        BeginPressAudition(picked, e);
        Refresh();
        return true;
    }

    // Sounds the note a click on the grid would place, at a semitone, for a key pressed beside the roll.
    public void AuditionSemitone(int semitone)
    {
        if (PlainNoteAt(0, semitone) is Note note)
        {
            KeyAuditioned?.Invoke(note);
        }
    }

    // Ends the note a key beside the roll started.
    public void EndKeyAudition()
    {
        NoteReleased?.Invoke();
    }

    // Sounds a note for as long as the press that started it is held, taking the pointer's capture.
    private void BeginPressAudition(Note note, PointerPressedEventArgs e)
    {
        _isAuditioning = true;
        e.Pointer.Capture(this);
        if (DoesAuditionNotes)
        {
            NoteAuditioned?.Invoke(note);
        }
    }

    // Keys off the note a press sounded, where one did.
    private void EndPressAudition()
    {
        if (!_isAuditioning)
        {
            return;
        }

        _isAuditioning = false;
        NoteReleased?.Invoke();
    }

    // Carries the pan, the sweep, a drag or the preview along with the cursor.
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _windowCursor.Remember(this, e);
        _isCursorInside = true;
        if (_isPanning)
        {
            return;
        }

        _cursorInGrid = e.GetPosition(this);
        if (_isSweepHeld)
        {
            UpdateSweep();
            return;
        }

        SetResizeModifier(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        if (_isDragging)
        {
            CommitPressSelection();
            UpdateDrag(_cursorInGrid);
            return;
        }

        if (_bendNoteAtPress is not null)
        {
            UpdateBendNodeDrag(_cursorInGrid);
            return;
        }

        UpdatePreviewAt(_cursorInGrid);
    }

    // Takes the preview away when the pointer leaves the grid.
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _isCursorInside = false;
        HidePreview();
    }

    // Lets go of the capture the released button's gesture holds, which is what ends it.
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (DoesButtonHoldCapture(e.InitialPressMouseButton))
        {
            e.Pointer.Capture(null);
        }
    }

    // Whether the gesture a button started is one whose capture the roll lets go of itself, the pan letting go of its own.
    private bool DoesButtonHoldCapture(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => IsDraggingNotes || _isAuditioning,
            MouseButton.Right => _isSweepHeld,
            _ => false,
        };
    }

    // Re-runs whichever drag is in progress from where the cursor is now, and answers whether one is still running.
    public bool FollowCursor()
    {
        return _windowCursor.FollowOver(this, _hasSweepMoved || IsDraggingNotes, FollowCursorWithGesture);
    }

    // Carries whichever drag is in progress, or the sweep, to where the cursor now is.
    private void FollowCursorWithGesture(Point cursor)
    {
        _cursorInGrid = cursor;
        if (_isDragging)
        {
            UpdateDrag(cursor);
            return;
        }

        if (_bendNoteAtPress is not null)
        {
            UpdateBendNodeDrag(cursor);
            return;
        }

        UpdateSweep();
    }

    // Re-runs the cursor over content that has moved under it: the drag where one is running, and the preview otherwise.
    public void ContentMoved()
    {
        if (FollowCursor() || _isPanning || !_isCursorInside)
        {
            return;
        }

        if (_windowCursor.PositionOver(this) is not Point cursor)
        {
            return;
        }

        _cursorInGrid = cursor;
        UpdatePreviewAt(cursor);
    }

    // Rescales the positions the roll remembers in the grid's own pixels by a zoom's two factors.
    public void RescaleRememberedPositions(double horizontalFactor, double verticalFactor)
    {
        _cursorInGrid = new Point(_cursorInGrid.X * horizontalFactor, _cursorInGrid.Y * verticalFactor);
        _dragAnchor = new Point(_dragAnchor.X * horizontalFactor, _dragAnchor.Y * verticalFactor);
        _sweepAnchor = new Point(_sweepAnchor.X * horizontalFactor, _sweepAnchor.Y * verticalFactor);
    }

    // Ends whatever the capture was held for: the drags, the pan, the sweep, the press and its audition.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (IsDraggingNotes)
        {
            DragEnded?.Invoke();
        }

        _isPanning = false;
        EndBendNodeDrag();
        EndSweep();
        EndDrag();
        EndPress();
        EndPressAudition();
    }

    // Selects what the first cursor movement of a plain press drags: the whole group where the press went down inside one, and the note otherwise.
    private void CommitPressSelection()
    {
        if (_pressedNote is null || _hasPressMoved)
        {
            return;
        }

        _hasPressMoved = true;
        if (_isDragging && _draggedNotes.Count > 1)
        {
            Reselect(_draggedNotes);
            return;
        }

        Select(_selectedChannel, _grabbedNote);
    }

    // Ends a press, selecting the one note it went down on where the cursor never moved.
    private void EndPress()
    {
        if (_pressedNote is Note pressed && !_hasPressMoved)
        {
            Select(_selectedChannel, pressed);
        }

        _pressedNote = null;
        _hasPressMoved = false;
    }
}
