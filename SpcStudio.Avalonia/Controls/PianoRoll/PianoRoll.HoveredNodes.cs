using Avalonia;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// Which notes show their bend joints: the selected notes on the channel being edited, and the note the cursor is over or on a joint of.
public sealed partial class PianoRoll
{
    // The channel and start tick of the note the cursor is over or on a joint of, or null where there is none.
    private (int Channel, int Tick)? _hoveredNoteKey;

    // The notes whose bend joints are drawn: the selected notes on the channel being edited, as a drag holds them, and then the hovered note.
    private List<Note> NotesShowingBendNodes()
    {
        var notes = Selection.Where(selected => selected.Channel == _selectedChannel).Select(selected => selected.Note).ToList();
        if (HoveredNote() is Note hovered && !notes.Contains(hovered))
        {
            notes.Add(hovered);
        }

        return notes;
    }

    // The note on the channel being edited the cursor is over or on a joint of, or null where there is none or a drag holds notes.
    private Note? HoveredNote()
    {
        if (IsDraggingNotes || _hoveredNoteKey is not (int channel, int tick) || channel != _selectedChannel)
        {
            return null;
        }

        return _song.NoteAt(channel, tick) is Note note && note.StartTick == tick ? note : null;
    }

    // Shows the joints of the note under a point or with a joint under it, or takes them off the roll where the point is null.
    private void ShowBendNodesOfNoteAt(Point? cursor)
    {
        var hovered = cursor is Point point ? NoteHoveredAt(point) : null;
        (int Channel, int Tick)? hoveredKey = hovered is Note note ? (_selectedChannel, note.StartTick) : null;
        if (hoveredKey == _hoveredNoteKey)
        {
            return;
        }

        _hoveredNoteKey = hoveredKey;
        InvalidateVisual();
    }

    // The note on the channel being edited with a joint under a point, or else the one drawn under it, or null.
    private Note? NoteHoveredAt(Point point)
    {
        if (BendNodeAt(point) is (Note owner, _))
        {
            return owner;
        }

        if (!TryGetCellAt(point, out int tick, out int semitone))
        {
            return null;
        }

        return NoteDrawnAt(_selectedChannel, point, tick, semitone);
    }
}
