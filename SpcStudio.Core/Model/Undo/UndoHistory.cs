namespace SpcStudio.Model;

// An undo and a redo stack of whole states, with the points the document was last saved at and last matched its MML at, and a run of edits from one
// source merged into one step.
public abstract class UndoHistory<TState> where TState : class, IUndoState<TState>
{
    public const int MaxDepth = 100;

    private readonly List<TState> _undoStates = [];
    private readonly List<TState> _redoStates = [];
    private readonly UndoDepthMark _savedMark = new();
    private readonly UndoDepthMark _mmlMatchMark = new();
    private TState _currentState;
    private object? _lastEditSource;

    protected UndoHistory(TState currentState)
    {
        _currentState = currentState;
    }

    // Whether there is a step to undo.
    public bool CanUndo
    {
        get
        {
            return _undoStates.Count > 0;
        }
    }

    // Whether there is a step to redo.
    public bool CanRedo
    {
        get
        {
            return _redoStates.Count > 0;
        }
    }

    // Whether the document holds edits the file on disk does not.
    public bool IsModified
    {
        get
        {
            return !_savedMark.IsAt(_undoStates.Count);
        }
    }

    // Whether the document is exactly what its MML holds, unchanged since it was last marked as matching it.
    public bool MatchesMml
    {
        get
        {
            return _mmlMatchMark.IsAt(_undoStates.Count);
        }
    }

    public TState Current
    {
        get
        {
            return _currentState;
        }
    }

    // Both marks, which every new step and every step dropped off the stack move alike.
    private UndoDepthMark[] Marks
    {
        get
        {
            return [_savedMark, _mmlMatchMark];
        }
    }

    // Records that the document has just been written to disk, so undoing back to this point drops the modified marker again.
    public void MarkSaved()
    {
        MarkHere(_savedMark);
    }

    // Records that the document has just been read from or written to its MML, so undoing back to this point counts as matching it again.
    public void MarkMatchesMml()
    {
        MarkHere(_mmlMatchMark);
    }

    // The state to go back to, which there has to be one of.
    public TState Undo()
    {
        return StepTo(_undoStates, _redoStates);
    }

    // The state to go forward to, which there has to be one of.
    public TState Redo()
    {
        return StepTo(_redoStates, _undoStates);
    }

    // Starts again from a state, with nothing to undo or redo and the state counted as saved.
    protected void ResetTo(TState state)
    {
        _undoStates.Clear();
        _redoStates.Clear();
        _currentState = state;
        _lastEditSource = null;
        _savedMark.SetAt(0);
        _mmlMatchMark.Lose();
    }

    // Starts again from a state holding a change no step here can put back, the document being modified until it is saved.
    protected void DiscardTo(TState state)
    {
        ResetTo(state);
        _savedMark.Lose();
    }

    // Takes a state as an undo step, the state an undo puts back being the one given, and merges it into the last step where the same source made both
    // or the source says it continues that step; false where nothing changed.
    protected bool RecordState(TState newState, TState stateToRestore, object? source)
    {
        if (newState.HoldsSameStateAs(_currentState))
        {
            return false;
        }

        bool isMergingIntoLastStep = ContinuesLastStep(source);
        _lastEditSource = source;
        if (!isMergingIntoLastStep)
        {
            PushUndoStep(stateToRestore);
        }

        _currentState = newState;
        return true;
    }

    // Whether an edit from a source joins the last step rather than starting one: where the source says it continues that step, or is the same source.
    protected bool ContinuesLastStep(object? source)
    {
        return source is IContinuingUndoSource continuing ? continuing.ContinuesStepOf(_lastEditSource) : source is not null && Equals(source, _lastEditSource);
    }

    // Marks the point the history stands at, the next edit starting a step of its own.
    private void MarkHere(UndoDepthMark mark)
    {
        mark.SetAt(_undoStates.Count);
        _lastEditSource = null;
    }

    // Steps through the history to the state on top of one stack, the state left behind going onto the other, and answers it.
    private TState StepTo(List<TState> stepsToTake, List<TState> stepsToReturn)
    {
        stepsToReturn.Add(_currentState);
        _currentState = PopTop(stepsToTake);
        _lastEditSource = null;
        return _currentState;
    }

    // Takes the top state off a stack.
    private static TState PopTop(List<TState> stack)
    {
        var top = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return top;
    }

    // Puts a state on the undo stack as a new step, discarding the redo branch and the oldest step past the depth limit.
    private void PushUndoStep(TState stateToRestore)
    {
        foreach (var mark in Marks)
        {
            mark.FollowNewStep(_undoStates.Count);
        }

        _redoStates.Clear();
        _undoStates.Add(stateToRestore);
        if (_undoStates.Count <= MaxDepth)
        {
            return;
        }

        _undoStates.RemoveAt(0);
        foreach (var mark in Marks)
        {
            mark.FollowOldestStepDropped();
        }
    }
}
