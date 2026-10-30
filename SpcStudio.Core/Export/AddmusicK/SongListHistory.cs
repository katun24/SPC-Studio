using SpcStudio.Model;

namespace SpcStudio.Export;

// The song list window's own undo and redo stacks, apart from every other window's: each state is both of its tables.
public sealed class SongListHistory(SongListState state) : UndoHistory<SongListState>(state)
{
    // Starts again from a list just read: nothing to undo or redo, and nothing to save.
    public void Reset(SongListState state)
    {
        ResetTo(state);
    }

    // Takes the tables as an edit left them as an undo step of their own; false where nothing changed.
    public bool Record(SongListState newState)
    {
        return RecordState(newState, Current, null);
    }
}
