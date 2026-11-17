namespace SpcStudio.Model;

// A source an edit is recorded under that decides for itself whether the edit continues the step the last edit made.
public interface IContinuingUndoSource
{
    // Whether an edit under this source continues the step the last edit, recorded under the given source, made.
    bool ContinuesStepOf(object? lastSource);
}
