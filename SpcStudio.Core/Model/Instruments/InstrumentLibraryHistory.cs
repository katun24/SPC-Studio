namespace SpcStudio.Model;

// The Instrument Picker's own undo and redo stacks, kept for as long as the app runs: each step is a change made to the instruments folder.
public sealed class InstrumentLibraryHistory() : UndoHistory<InstrumentLibraryState>(new InstrumentLibraryState(null))
{
    // The change an undo would put back, or null where there is none.
    public InstrumentLibraryChange? ChangeToUndo
    {
        get
        {
            return CanUndo ? Current.LastChange : null;
        }
    }

    // Takes a change just made on disk as an undo step of its own.
    public void Record(InstrumentLibraryChange change)
    {
        RecordState(new InstrumentLibraryState(change), Current, null);
    }
}
