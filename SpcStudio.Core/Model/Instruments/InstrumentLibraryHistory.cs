namespace SpcStudio.Model;

// The Instrument Picker's own undo and redo stacks, kept for as long as the app runs: each step is the changes made to the instruments folder by one act.
public sealed class InstrumentLibraryHistory() : UndoHistory<InstrumentLibraryState>(new InstrumentLibraryState([]))
{
    // The changes an undo would put back, in the order they were made, or none where there is nothing to undo.
    public IReadOnlyList<InstrumentLibraryChange> ChangesToUndo
    {
        get
        {
            return CanUndo ? Current.Changes : [];
        }
    }

    // Takes a change just made on disk as an undo step, joining the last step where the source continues it.
    public void Record(InstrumentLibraryChange change, object? source)
    {
        IReadOnlyList<InstrumentLibraryChange> stepChanges = ContinuesLastStep(source) ? [.. Current.Changes, change] : [change];
        RecordState(new InstrumentLibraryState(stepChanges), Current, source);
    }
}
