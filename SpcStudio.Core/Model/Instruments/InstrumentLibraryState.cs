namespace SpcStudio.Model;

// A step of the instruments folder's undo history: the change that led to it, or null at the start.
public sealed record InstrumentLibraryState(InstrumentLibraryChange? LastChange) : IUndoState<InstrumentLibraryState>
{
    // Whether the two are the same step.
    public bool HoldsSameStateAs(InstrumentLibraryState other)
    {
        return ReferenceEquals(this, other);
    }
}
