using Avalonia;
using Avalonia.Input;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// The bend joint B puts on a note: the preview of it while the key is held, and the press that puts it there and takes hold of it.
public sealed partial class PianoRoll
{
    private bool _isNodeInsertKeyHeld;

    // The joint B would put on the note under the cursor, or null where there is none to put.
    private BendNodeInsert? _nodeInsertPreview;

    // Tells the roll whether B is held, which shows the joint preview in place of the note preview.
    public void SetNodeInsertKey(bool isHeld)
    {
        if (isHeld == _isNodeInsertKeyHeld)
        {
            return;
        }

        _isNodeInsertKeyHeld = isHeld;
        if (IsGestureRunning || !_isCursorInside)
        {
            return;
        }

        UpdatePreviewAt(_cursorInGrid);
    }

    // Draws the joint B would put at a point, or takes it off the roll where the point is null.
    private void ShowNodeInsertPreview(Point? cursor)
    {
        var preview = cursor is Point point ? NodeInsertAt(point) : null;
        if (preview == _nodeInsertPreview)
        {
            return;
        }

        _nodeInsertPreview = preview;
        InvalidateVisual();
    }

    // The joint a B press at a point puts on the edited channel's note at the nearest snap position, or null where no note covers or ends on that position or the joint cannot go there.
    private BendNodeInsert? NodeInsertAt(Point point)
    {
        if (!TryGetCellAt(point, out _, out _))
        {
            return null;
        }

        int tick = RollMetrics.SnappedTickAtX(_song, point.X);
        if (NoteTakingNodeAt(tick) is not Note noteInSong)
        {
            return null;
        }

        if (BendNodes.WithNodeInserted(noteInSong, tick - noteInSong.StartTick) is not (Note noteWithNode, int nodeIndex))
        {
            return null;
        }

        return new BendNodeInsert(noteInSong, noteWithNode, nodeIndex);
    }

    // The edited channel's note a joint at a tick goes on: the one sounding there - the later of two meeting there - or else the one ending there.
    private Note? NoteTakingNodeAt(int tick)
    {
        if (_song.NoteAt(_selectedChannel, tick) is Note soundingNote)
        {
            return soundingNote;
        }

        return _song.NoteAt(_selectedChannel, tick - 1) is Note endingNote && endingNote.EndTick() == tick ? endingNote : null;
    }

    // Puts the joint B shows at a press on its note and takes hold of it, or does nothing where there is none to put.
    private void InsertBendNodeAndTakeHold(Point cursor, PointerPressedEventArgs e)
    {
        if (NodeInsertAt(cursor) is not BendNodeInsert insert)
        {
            return;
        }

        BeginBendNodeDrag(insert.NoteInSong, insert.NoteWithNode, insert.NodeIndex, cursor, e);
    }

    // One joint B puts on a note: the note as the song holds it, the note with the joint on its path, and the joint's index among the note's joints.
    private readonly record struct BendNodeInsert(Note NoteInSong, Note NoteWithNode, int NodeIndex);
}
