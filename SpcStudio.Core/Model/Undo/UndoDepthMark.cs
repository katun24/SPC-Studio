namespace SpcStudio.Model;

// A point in an undo history, kept as the undo stack's depth there, and lost once the history can no longer reach it.
public sealed class UndoDepthMark
{
    private const int UnreachableDepth = -1;

    private int _depth;

    // Whether the history stands at the marked point, given the undo stack's depth now.
    public bool IsAt(int undoDepth)
    {
        return _depth == undoDepth;
    }

    // Marks the point at a given undo stack depth.
    public void SetAt(int undoDepth)
    {
        _depth = undoDepth;
    }

    // Marks the point as one the history can no longer reach.
    public void Lose()
    {
        _depth = UnreachableDepth;
    }

    // Loses the point where a new step replaces the redo branch it stood on, given the undo stack's depth before the step.
    public void FollowNewStep(int undoDepthBeforeStep)
    {
        if (_depth > undoDepthBeforeStep)
        {
            Lose();
        }
    }

    // Moves the point down with the stack as its oldest step falls off, losing it where it was that step.
    public void FollowOldestStepDropped()
    {
        _depth = Math.Max(UnreachableDepth, _depth - 1);
    }
}
