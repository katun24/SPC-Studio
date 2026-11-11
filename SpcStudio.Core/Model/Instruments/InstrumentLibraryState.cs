namespace SpcStudio.Model;

// A step of the instruments folder's undo history: the changes the step that led to it made, in the order made, or none at the start.
public sealed record InstrumentLibraryState(IReadOnlyList<InstrumentLibraryChange> Changes) : IUndoState<InstrumentLibraryState>
{
    // Whether the two are the same step.
    public bool HoldsSameStateAs(InstrumentLibraryState other)
    {
        return ReferenceEquals(this, other);
    }
}
