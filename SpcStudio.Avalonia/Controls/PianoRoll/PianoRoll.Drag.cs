using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The roll's drags: a note or a group moved and Shift-resized by the left button, a bend node carried along its chain, and the drop.
public sealed partial class PianoRoll : Control
{
    private bool _isDragging;
    private bool _didPressPlaceNotes;
    private Point _dragAnchor;
    private Note _grabbedNoteAtPress;
    private Note _grabbedNote;
    private int _draggedNodeIndex;

    // The notes being dragged with the channel each is on, where the drag has carried them; empty except during a drag.
    private readonly List<(int Channel, Note Note)> _draggedNotes = [];

    // How Shift switches a note drag between moving and resizing, and starts and ends the preview's resize.
    private readonly ShiftResize<NotePlacement> _shiftResize;

    // The dragged notes as the press found them, in the same order.
    private readonly List<(int Channel, Note Note)> _notesAtPress = [];

    // The dragged notes as the current mode measures against, in the same order.
    private readonly List<(int Channel, Note Note)> _dragBaselines = [];

    // The selection a Ctrl press on empty grid kept beside the notes it placed, sorted, which the drop selects again; empty for every other drag.
    private readonly List<(int Channel, int Tick)> _keysKeptByPlacement = [];

    // Which of the dragged notes the press grabbed, and so the one the cursor drives.
    private int _grabbedIndex;

    // The grabbed note as the current mode measures against.
    private Note _grabbedBaseline;

    // Where the note being resized starts, dragged along by a cursor taken left of it.
    private int _resizeStartTick;

    // The note whose bend node is being dragged, as the press found it, or null while no node is.
    private Note? _bendNoteAtPress;

    // The note whose bend node is being dragged, as the song held it before a B press put the node on it.
    private Note _bendNoteBeforePress;

    // The furthest left the dragged bend node has been taken since the press, in ticks from its note's start.
    private int _leftmostDraggedNodeTick;

    // The note whose bend node is being dragged, with the node where the drag has carried it.
    private Note _draggedBendNote;

    // Whether a drag holds notes out of the song - notes being moved or resized, or the note whose bend node is being dragged - which also makes the selection answer with where they have got to.
    public bool IsDraggingNotes
    {
        get
        {
            return _isDragging || _bendNoteAtPress is not null;
        }
    }

    // Whether a note drag is running, which Shift switches between moving and resizing.
    bool IShiftResizeSurface.IsDragRunning
    {
        get
        {
            return _isDragging;
        }
    }

    // Whether the drag carries notes a Ctrl press placed while the selection from before the press stays selected beside them.
    private bool IsDragKeepingSelection
    {
        get
        {
            return _keysKeptByPlacement.Count > 0;
        }
    }

    // Drags a note already in the song, with the rest of the selection where the press went down inside it, resizing at the press where Shift is down.
    private void BeginDragOfExistingNote(Note grabbedNote, Point anchor, PointerPressedEventArgs e)
    {
        _notesAtPress.Clear();
        if (_selectedKeys.Count > 1 && _selectedKeySet.Contains((_selectedChannel, grabbedNote.StartTick)))
        {
            _notesAtPress.AddRange(Selection);
        }
        else
        {
            _notesAtPress.Add((_selectedChannel, grabbedNote));
        }

        _grabbedIndex = Math.Max(_notesAtPress.FindIndex(member => member.Channel == _selectedChannel && member.Note.StartTick == grabbedNote.StartTick), 0);
        _didPressPlaceNotes = false;
        BeginDrag(grabbedNote, anchor, e);
        if (_shiftResize.IsDragResizing)
        {
            UpdateDrag(anchor);
        }

        RaiseSelectionChanged();
    }

    // Drags the notes the press has just placed, grabbed by the first of them.
    private void BeginDragOfPlacedGroup(IReadOnlyList<NotePlacement> group, Point anchor, PointerPressedEventArgs e)
    {
        _notesAtPress.Clear();
        _notesAtPress.AddRange(group.Select(member => member.ChannelAndNote));
        _grabbedIndex = 0;
        _didPressPlaceNotes = true;
        BeginDrag(group[0].Note, anchor, e);
    }

    // Lifts the notes the press took hold of out of the song and follows the cursor with them until the button comes up.
    private void BeginDrag(Note grabbedNote, Point anchor, PointerPressedEventArgs e)
    {
        foreach (var (channel, member) in _notesAtPress)
        {
            _song.LiftNote(channel, member.StartTick);
        }

        _dragBaselines.Clear();
        _dragBaselines.AddRange(_notesAtPress);
        _draggedNotes.Clear();
        _draggedNotes.AddRange(_notesAtPress);
        _placementPreview.Hide();

        _isDragging = true;
        _shiftResize.IsDragResizing = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _grabbedNoteAtPress = grabbedNote;
        _grabbedBaseline = grabbedNote;
        _grabbedNote = grabbedNote;
        _resizeStartTick = grabbedNote.StartTick;
        CaptureDragFrom(anchor, e);
        RaiseNotesDragged();
    }

    // Measures a drag from where its press went down and takes the pointer's capture.
    private void CaptureDragFrom(Point anchor, PointerPressedEventArgs e)
    {
        _dragAnchor = anchor;
        _cursorInGrid = anchor;
        e.Pointer.Capture(this);
    }

    // Tells the window which notes the drag holds and where it has them.
    private void RaiseNotesDragged()
    {
        NotesDragged?.Invoke(NotesHeldByDrag);
    }

    // Tells a drag in progress, or the preview under the cursor, whether Shift is down, switching between moving and resizing.
    public void SetResizeModifier(bool isShiftHeld)
    {
        _shiftResize.SetModifier(isShiftHeld);
    }

    // Turns the resize off until Shift is let go.
    public void SuppressResize()
    {
        _shiftResize.Suppress();
    }

    // Makes the dragged notes as they now stand the drag's baseline, measured from where the cursor now is.
    void IShiftResizeSurface.RebaseDrag()
    {
        RebaseGrabbedNote(_grabbedNote);
        _dragBaselines.AddRange(_draggedNotes);
    }

    // Carries the dragged notes to where the cursor now is.
    void IShiftResizeSurface.FollowCursorWithDrag()
    {
        UpdateDrag(_cursorInGrid);
    }

    // Puts the preview back under the cursor.
    void IShiftResizeSurface.ShowPreviewAtCursor()
    {
        UpdatePreviewAt(_cursorInGrid);
    }

    // Makes the preview as it now stands the resize's baseline, measured from where the cursor now is, its first member being the one the cursor drives.
    void IShiftResizeSurface.RebasePreviewResize()
    {
        RebaseGrabbedNote(_placementPreview.Members[0].Note);
        _dragBaselines.AddRange(_placementPreview.Members.Select(member => member.ChannelAndNote));
    }

    // Makes a note the grabbed note's baseline, measured from where the cursor now is, and empties the group's baselines.
    private void RebaseGrabbedNote(Note grabbedNote)
    {
        _grabbedBaseline = grabbedNote;
        _dragAnchor = _cursorInGrid;
        _resizeStartTick = grabbedNote.StartTick;
        _dragBaselines.Clear();
    }

    // Carries the dragged notes to where the cursor now is, by the current mode, and sounds the grabbed note where its pitch changed.
    private void UpdateDrag(Point cursor)
    {
        DragAt?.Invoke(cursor.X, cursor.Y);

        var (startDelta, lengthDelta, rowDelta) = _shiftResize.IsDragResizing ? ClampedResizeDeltas(cursor) : ClampedMoveDeltas(cursor);
        var draggedNotes = new List<(int Channel, Note Note)>(_dragBaselines.Count);
        foreach (var (channel, baseline) in _dragBaselines)
        {
            draggedNotes.Add((channel, _song.NoteAsPlaced(WithDeltas(baseline, startDelta, lengthDelta, rowDelta))));
        }

        if (draggedNotes.SequenceEqual(_draggedNotes))
        {
            return;
        }

        Note grabbedNote = draggedNotes[_grabbedIndex].Note;
        bool hasPitchChanged = grabbedNote.Semitone != _grabbedNote.Semitone;
        _grabbedNote = grabbedNote;
        _draggedNotes.Clear();
        _draggedNotes.AddRange(draggedNotes);

        int rollLengthTicksBefore = _song.RollLengthTicks;
        _song.SetLiftedNotes([.. _draggedNotes.Select(dragged => dragged.Note)]);
        ReportRollResize(rollLengthTicksBefore);

        RaiseNotesDragged();
        CommitPressSelection();
        if (hasPitchChanged && DoesAuditionNotes)
        {
            NoteAuditioned?.Invoke(grabbedNote);
        }

        Refresh();
    }

    // How far a move to the cursor carries the group, in whole snap steps from the one it was grabbed in, held on the roll.
    private (int StartDelta, int LengthDelta, int RowDelta) ClampedMoveDeltas(Point cursor)
    {
        return HeldOnRoll(RollMetrics.SnapStepsMovedTicks(_song, _dragAnchor.X, cursor.X), 0, RowDeltaAt(cursor));
    }

    // How far a resize to the cursor moves the grabbed note's start, how much longer it makes it and how many rows down it carries it, held on the roll.
    private (int StartDelta, int LengthDelta, int RowDelta) ClampedResizeDeltas(Point cursor)
    {
        Note resizedNote = ResizedToCursor(cursor);
        return HeldOnRoll(resizedNote.StartTick - _grabbedBaseline.StartTick, resizedNote.LengthTicks - _grabbedBaseline.LengthTicks, RowDeltaAt(cursor));
    }

    // How many rows down the cursor has moved from where the drag measures from.
    private int RowDeltaAt(Point cursor)
    {
        return RollMetrics.StepIndexAt(cursor.Y, RollMetrics.CellHeight) - RollMetrics.StepIndexAt(_dragAnchor.Y, RollMetrics.CellHeight);
    }

    // The deltas held so that no member of the group is carried off the front of the song or past the roll's highest or lowest pitch.
    private (int StartDelta, int LengthDelta, int RowDelta) HeldOnRoll(int startDelta, int lengthDelta, int rowDelta)
    {
        var (earliestTick, lowestSemitone, highestSemitone) = ReachOfGroup(_dragBaselines.Select(member => member.Note));
        int heldStartDelta = GroupStep.HeldOffSongStart(startDelta, earliestTick);
        int heldRowDelta = Math.Clamp(rowDelta, highestSemitone - Song.HighestSemitone, lowestSemitone - Song.LowestSemitone);
        return (heldStartDelta, lengthDelta, heldRowDelta);
    }

    // The earliest tick a group of notes starts at, and the lowest and highest pitches its notes and their bends reach.
    private static (int EarliestTick, int LowestSemitone, int HighestSemitone) ReachOfGroup(IEnumerable<Note> notes)
    {
        int earliestTick = int.MaxValue;
        int lowestSemitone = int.MaxValue;
        int highestSemitone = int.MinValue;
        foreach (var note in notes)
        {
            earliestTick = Math.Min(earliestTick, note.StartTick);
            var (lowestOffset, highestOffset) = PitchBend.Reach(note.Bends);
            lowestSemitone = Math.Min(lowestSemitone, note.Semitone + lowestOffset);
            highestSemitone = Math.Max(highestSemitone, note.Semitone + highestOffset);
        }

        return (earliestTick, lowestSemitone, highestSemitone);
    }

    // One member of the group with the drag's deltas applied, never shortened below one snap step or its own length.
    private Note WithDeltas(Note baseline, int startDelta, int lengthDelta, int rowDelta)
    {
        return baseline.WithLength(SnapSpan.Lengthened(_song, baseline.LengthTicks, lengthDelta)) with
        {
            StartTick = baseline.StartTick + startDelta,
            Semitone = baseline.Semitone - rowDelta,
        };
    }

    // The grabbed note stretched from where the resize starts to the end of the snap step the cursor is in, the start dragged along by a cursor taken left of it.
    private Note ResizedToCursor(Point cursor)
    {
        var (startTick, lengthTicks) = SnapSpan.ResizedToTick(_song, (int)Math.Floor(RollMetrics.ExactTickAtX(cursor.X)), ref _resizeStartTick, _grabbedBaseline.LengthTicks);
        return _grabbedBaseline.WithLength(lengthTicks) with { StartTick = startTick };
    }

    // Drops the dragged notes where the drag left them, in one placement, and reports the edit where the drop changed anything.
    private void EndDrag()
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        var droppedNotes = new List<(int Channel, Note Note)>(_draggedNotes);

        _song.PlaceNotes(droppedNotes);
        Rekey(KeysBesideKeptSelection(droppedNotes));
        _keysKeptByPlacement.Clear();
        _draggedNotes.Clear();

        if (_grabbedNote.LengthTicks != _grabbedNoteAtPress.LengthTicks)
        {
            _placedNoteLengthTicks = _grabbedNote.LengthTicks;
        }

        if (_didPressPlaceNotes || !droppedNotes.SequenceEqual(_notesAtPress))
        {
            SongEdited?.Invoke();
        }

        Refresh();
    }

    // The bend node under a point on the channel being edited and the note it belongs to, the nearest where several are, or null where there is none.
    private (Note Note, int Index)? BendNodeAt(Point point)
    {
        double nodeRadius = NoteShape.HalfBandHeight(Space.Px2);
        (Note, int)? nearestNode = null;
        double nearestDistance = double.MaxValue;
        foreach (var note in _song.Notes(_selectedChannel))
        {
            if (IsStartRightOf(note, point.X + nodeRadius))
            {
                break;
            }

            if (IsDrawnEndLeftOf(note, point.X - nodeRadius))
            {
                continue;
            }

            var nodes = BendNodes.NodesOf(note);
            for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
            {
                double distance = Point.Distance(NoteShape.BendNodeCenter(note, nodes[nodeIndex]), point);
                if (distance > nodeRadius || distance > nearestDistance)
                {
                    continue;
                }

                nearestNode = (note, nodeIndex);
                nearestDistance = distance;
            }
        }

        return nearestNode;
    }

    // Selects the note a pressed bend node belongs to, lifts it out of the song and follows the cursor with the node until the button comes up - the note
    // as the press found it being the song's own, or the song's with a node a B press has just put on it.
    private void BeginBendNodeDrag(Note noteInSong, Note noteAtPress, int nodeIndex, Point anchor, PointerPressedEventArgs e)
    {
        Select(_selectedChannel, noteInSong);
        _placedNoteLengthTicks = noteInSong.LengthTicks;

        _bendNoteBeforePress = noteInSong;
        _bendNoteAtPress = noteAtPress;
        _draggedNodeIndex = nodeIndex;
        _leftmostDraggedNodeTick = BendNodes.NodesOf(noteAtPress)[nodeIndex].Tick;
        _draggedBendNote = NodeDragNoteAsDropped(noteAtPress);

        _song.LiftNote(_selectedChannel, noteInSong.StartTick);
        _song.SetLiftedNotes([_draggedBendNote]);

        HidePreview();
        CaptureDragFrom(anchor, e);
        if (_draggedBendNote != noteInSong)
        {
            RaiseNotesDragged();
        }

        Refresh();
    }

    // Carries the dragged bend node to the snap step the cursor is in and the rows it has moved, where the chain can hold it there - along its row alone
    // where it stands beside a level stretch too long for a ramp, and taking the note's end with it where it is on the end.
    private void UpdateBendNodeDrag(Point cursor)
    {
        if (_bendNoteAtPress is not Note noteAtPress)
        {
            return;
        }

        var nodeAtPress = BendNodes.NodesOf(noteAtPress)[_draggedNodeIndex];
        int nodeTick = SnappedNodeTickAt(cursor, noteAtPress, nodeAtPress);
        int nodeSemitone = nodeAtPress.Semitone - RowDeltaAt(cursor);
        _leftmostDraggedNodeTick = Math.Min(_leftmostDraggedNodeTick, nodeTick);
        bool isNodeBackAtPress = nodeTick == nodeAtPress.Tick && nodeSemitone == nodeAtPress.Semitone && _leftmostDraggedNodeTick == nodeAtPress.Tick;
        var nodeMovedNote = isNodeBackAtPress ? noteAtPress : BendNodes.WithNodeDragged(noteAtPress, _draggedNodeIndex, nodeTick, nodeSemitone, _leftmostDraggedNodeTick);
        if (nodeMovedNote is not Note unclaimedNote)
        {
            return;
        }

        var movedNote = NodeDragNoteAsDropped(unclaimedNote);
        if (movedNote == _draggedBendNote)
        {
            return;
        }

        _draggedBendNote = movedNote;
        _song.SetLiftedNotes([movedNote]);
        RaiseNotesDragged();
        Refresh();
    }

    // The tick from its note's start the dragged bend node goes to at a cursor: the snap position nearest the cursor, or the last one short of a neighbor
    // the cursor has gone past, or the tick it was pressed on where no snap position is free between its neighbors.
    private int SnappedNodeTickAt(Point cursor, Note noteAtPress, BendNode nodeAtPress)
    {
        int startTick = noteAtPress.StartTick;
        var (lowestTick, highestTick) = BendNodes.TickRangeOf(noteAtPress, _draggedNodeIndex);
        int songHighestTick = highestTick > int.MaxValue - startTick ? int.MaxValue : startTick + highestTick;
        int? snappedTick = _song.SnapInside(RollMetrics.SnappedTickAtX(_song, cursor.X), startTick + lowestTick, songHighestTick);
        return snappedTick is int tick ? tick - startTick : nodeAtPress.Tick;
    }

    // Drops the dragged bend node's note where the drag left it, and reports the edit where the node moved.
    private void EndBendNodeDrag()
    {
        if (_bendNoteAtPress is not Note noteAtPress)
        {
            return;
        }

        _bendNoteAtPress = null;
        var droppedNote = _draggedBendNote;
        _song.PlaceNote(_selectedChannel, droppedNote);
        Rekey([(_selectedChannel, droppedNote.StartTick)]);

        if (droppedNote != _bendNoteBeforePress)
        {
            SongEdited?.Invoke();
        }

        Refresh();
    }

    // A note a node drag has made as the drop will place it: its chain claimed as the note's own wherever it differs from the song's, and then landed as any placed note is.
    private Note NodeDragNoteAsDropped(Note draggedNote)
    {
        if (draggedNote == _bendNoteBeforePress)
        {
            return draggedNote;
        }

        return _song.NoteAsPlaced(draggedNote with { BendSource = NoteSource.Note });
    }
}
