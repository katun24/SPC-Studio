using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The roll's selection: which notes are selected, the gestures that select them, and the right button's sweep rectangle.
public sealed partial class PianoRoll : Control
{
    private readonly HashSet<(int Channel, int Tick)> _selectedKeySet = [];
    private bool _isSweepHeld;
    private Point _sweepAnchor;
    private bool _hasSweepMoved;
    private bool _isSweepToggling;
    private bool _doesSweepReachEveryChannel;

    // The selected notes as a channel and a start tick each, sorted by channel and then by tick.
    private readonly List<(int Channel, int Tick)> _selectedKeys = [];

    // Whether the sweep rectangle has moved since its press.
    public bool IsSweeping
    {
        get
        {
            return _hasSweepMoved;
        }
    }

    // The notes a drag holds out of the song with their channels, as the drag has them: the dragged notes, or the note whose bend node is dragged.
    private IReadOnlyList<(int Channel, Note Note)> NotesHeldByDrag
    {
        get
        {
            return _isDragging ? _draggedNotes : [(_selectedChannel, _draggedBendNote)];
        }
    }

    // The selected note with its channel, or null where none or several are selected.
    public (int Channel, Note Note)? SelectedChannelNote
    {
        get
        {
            return Selection is [var onlyNote] ? onlyNote : null;
        }
    }

    // The selected notes with their channels, as they stand in the song now or as a drag holds them, beside the ones a Ctrl placement kept.
    public IReadOnlyList<(int Channel, Note Note)> Selection
    {
        get
        {
            if (!IsDraggingNotes)
            {
                return NotesAtKeys(_selectedKeys);
            }

            if (!IsDragKeepingSelection)
            {
                return NotesHeldByDrag;
            }

            return [.. NotesHeldByDrag, .. NotesAtKeys(_keysKeptByPlacement)];
        }
    }

    // The selected notes without their channels.
    public IReadOnlyList<Note> SelectedNotes
    {
        get
        {
            var selection = Selection;
            var notes = new List<Note>(selection.Count);
            foreach (var (_, note) in selection)
            {
                notes.Add(note);
            }

            return notes;
        }
    }

    // How many notes are selected.
    public int SelectionCount
    {
        get
        {
            return Selection.Count;
        }
    }

    // The channels the selection covers, one bit per channel.
    public int SelectionChannels
    {
        get
        {
            int channelBits = 0;
            foreach (var (channel, _) in Selection)
            {
                channelBits |= 1 << channel;
            }

            return channelBits;
        }
    }

    // How many channels the selection covers.
    public int SelectionChannelCount
    {
        get
        {
            return System.Numerics.BitOperations.PopCount((uint)SelectionChannels);
        }
    }

    // The notes the song has at a sorted set of channels and start ticks, with their channels, skipping the keys it has no note at.
    private List<(int Channel, Note Note)> NotesAtKeys(IReadOnlyList<(int Channel, int Tick)> sortedKeys)
    {
        var notes = new List<(int, Note)>(sortedKeys.Count);
        IReadOnlyList<Note> channelNotes = [];
        int walkedChannel = -1;
        int noteIndex = 0;

        foreach (var (keyChannel, keyTick) in sortedKeys)
        {
            if (keyChannel != walkedChannel)
            {
                walkedChannel = keyChannel;
                channelNotes = keyChannel < _song.ChannelCount ? _song.Notes(keyChannel) : [];
                noteIndex = 0;
            }

            while (noteIndex < channelNotes.Count && channelNotes[noteIndex].StartTick < keyTick)
            {
                noteIndex++;
            }

            if (noteIndex < channelNotes.Count && channelNotes[noteIndex].StartTick == keyTick)
            {
                notes.Add((keyChannel, channelNotes[noteIndex]));
            }
        }

        return notes;
    }

    // Selects nothing.
    public void ClearSelection()
    {
        Select([]);
    }

    // Selects one note on a channel, or nothing where it is null.
    private void Select(int channel, Note? note)
    {
        Select(note is Note selectedNote ? [(channel, selectedNote.StartTick)] : []);
    }

    // Replaces the selection with a set of channels and start ticks a gesture chose, sorted, and raises the change where it differs.
    private void Select(IEnumerable<(int Channel, int Tick)> keys)
    {
        if (TryReplaceSelectedKeys(keys))
        {
            RaiseSelectionChanged();
        }
    }

    // Re-keys the selection onto a set of channels and start ticks an edit left the selected notes at, and raises the edit.
    private void Rekey(IEnumerable<(int Channel, int Tick)> keys)
    {
        TryReplaceSelectedKeys(keys);
        RaiseSelectedNotesEdited();
    }

    // Replaces the selected keys with a set of channels and start ticks, sorted, and answers whether they differ from the ones replaced.
    private bool TryReplaceSelectedKeys(IEnumerable<(int Channel, int Tick)> keys)
    {
        var distinctKeys = keys.Distinct().ToList();
        distinctKeys.Sort();
        if (distinctKeys.SequenceEqual(_selectedKeys))
        {
            return false;
        }

        _placementPreview.ForgetUniformLength();
        _selectedKeys.Clear();
        _selectedKeys.AddRange(distinctKeys);
        _selectedKeySet.Clear();
        _selectedKeySet.UnionWith(distinctKeys);
        return true;
    }

    // Takes a note into the selection or out of it again, and answers whether it went in.
    private bool ToggleInSelection(int channel, Note note)
    {
        var key = (channel, note.StartTick);
        bool isAdded = !_selectedKeySet.Contains(key);
        FlipInSelection([key]);
        return isAdded;
    }

    // Takes each of a set of notes out of the selection where it is in it and into it where it is not.
    private void FlipInSelection(IEnumerable<(int Channel, int Tick)> keys)
    {
        var flippedKeys = new List<(int Channel, int Tick)>(_selectedKeys);
        foreach (var key in keys)
        {
            if (!flippedKeys.Remove(key))
            {
                flippedKeys.Add(key);
            }
        }

        Select(flippedKeys);
    }

    // Raises the selection change with the selected notes.
    private void RaiseSelectionChanged()
    {
        SelectionChanged?.Invoke(SelectedNotes);
    }

    // Raises the edit of the selected notes with the selected notes.
    private void RaiseSelectedNotesEdited()
    {
        SelectedNotesEdited?.Invoke(SelectedNotes);
    }

    // Selects a set of notes a gesture chose or made.
    private void Reselect(IEnumerable<(int Channel, Note Note)> notes)
    {
        Select(KeysOf(notes));
    }

    // A set of notes placed or dropped by a drag as selection keys, beside the selection a Ctrl placement kept.
    private IEnumerable<(int Channel, int Tick)> KeysBesideKeptSelection(IEnumerable<(int Channel, Note Note)> notes)
    {
        return _keysKeptByPlacement.Concat(KeysOf(notes));
    }

    // A set of notes as selection keys.
    private static IEnumerable<(int Channel, int Tick)> KeysOf(IEnumerable<(int Channel, Note Note)> notes)
    {
        return notes.Select(channelNote => (channelNote.Channel, channelNote.Note.StartTick));
    }

    // Drops the selected ticks the song no longer has a note at, keeps the rest and raises the edit.
    public void PruneSelection()
    {
        Rekey(KeysOf(Selection));
    }

    // Selects every note on the channel being edited, and answers whether there were any.
    public bool SelectAllOnEditedChannel()
    {
        if (IsDraggingNotes)
        {
            return false;
        }

        var notes = _song.Notes(_selectedChannel);
        if (notes.Count == 0)
        {
            return false;
        }

        Select(notes.Select(note => (_selectedChannel, note.StartTick)));
        InvalidateVisual();
        return true;
    }

    // Selects every note in the song on every channel, and answers whether there were any.
    public bool SelectAllChannels()
    {
        if (IsDraggingNotes)
        {
            return false;
        }

        var keys = KeysOfNotesMatching((_, _) => true);
        if (keys.Count == 0)
        {
            return false;
        }

        Select(keys);
        InvalidateVisual();
        return true;
    }

    // Selects every note in the song a filter answers true for, and nothing where none does.
    public void SelectMatching(Func<int, Note, bool> match)
    {
        if (IsDraggingNotes)
        {
            return;
        }

        Select(KeysOfNotesMatching(match));
        InvalidateVisual();
    }

    // Every note in the song a filter answers true for, as selection keys.
    private List<(int Channel, int Tick)> KeysOfNotesMatching(Func<int, Note, bool> match)
    {
        return [.. KeysOf(_song.NotesWithChannels().Where(one => match(one.Channel, one.Note)))];
    }

    // Starts a sweep rectangle from where the right button went down, reading Ctrl and Alt off the press, and clears the selection unless Ctrl was held.
    private void BeginSweep(Point anchor, PointerPressedEventArgs e)
    {
        _isSweepHeld = true;
        _hasSweepMoved = false;
        _isSweepToggling = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        _doesSweepReachEveryChannel = _isShowingOtherChannels && e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        _sweepAnchor = anchor;
        _cursorInGrid = anchor;
        HidePreview();
        e.Pointer.Capture(this);
        if (!_isSweepToggling)
        {
            ClearSelection();
            InvalidateVisual();
        }
    }

    // Carries the sweep to where the cursor now is, asking the view to follow it.
    private void UpdateSweep()
    {
        _hasSweepMoved = true;
        DragAt?.Invoke(_cursorInGrid.X, _cursorInGrid.Y);
        InvalidateVisual();
    }

    // Ends the sweep: a rectangle selects or flips what it touched, and a press that never moved leaves the selection as its press left it.
    private void EndSweep()
    {
        if (!_isSweepHeld)
        {
            return;
        }

        bool hasMoved = _hasSweepMoved;
        bool isToggling = _isSweepToggling;
        IEnumerable<int> sweptChannels = _doesSweepReachEveryChannel ? Enumerable.Range(0, _song.ChannelCount) : new[] { _selectedChannel };
        _isSweepHeld = false;
        _hasSweepMoved = false;
        _isSweepToggling = false;
        _doesSweepReachEveryChannel = false;
        DragEnded?.Invoke();
        if (hasMoved && isToggling)
        {
            FlipInSelection(SweptKeys(sweptChannels));
        }
        else if (hasMoved)
        {
            Select(SweptKeys(sweptChannels));
        }

        InvalidateVisual();
    }

    // The rectangle the sweep covers in the grid's own coordinates, held inside the grid.
    private Rect SweepRect()
    {
        double left = Math.Clamp(Math.Min(_sweepAnchor.X, _cursorInGrid.X), 0, GridWidth);
        double right = Math.Clamp(Math.Max(_sweepAnchor.X, _cursorInGrid.X), 0, GridWidth);
        double top = Math.Clamp(Math.Min(_sweepAnchor.Y, _cursorInGrid.Y), 0, RollMetrics.GridHeight);
        double bottom = Math.Clamp(Math.Max(_sweepAnchor.Y, _cursorInGrid.Y), 0, RollMetrics.GridHeight);
        return new Rect(left, top, right - left, bottom - top);
    }

    // Every note on a set of channels the sweep rectangle touches any part of as drawn, as selection keys.
    private List<(int Channel, int Tick)> SweptKeys(IEnumerable<int> channels)
    {
        var rect = SweepRect();
        var sweptKeys = new List<(int Channel, int Tick)>();
        foreach (int channel in channels)
        {
            foreach (var note in _song.Notes(channel))
            {
                if (NoteShape.IsTouchedByRect(note, rect, Space.Px2))
                {
                    sweptKeys.Add((channel, note.StartTick));
                }
            }
        }

        return sweptKeys;
    }
}
