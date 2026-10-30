namespace SpcStudio.Model;

// The sample editor's own undo and redo stacks, apart from the rest of the app's: each state is the song's samples and the view they were edited through.
public sealed class SampleHistory : UndoHistory<SampleSnapshot>
{
    // A history starting from a song's samples as they stand.
    public SampleHistory(Song song) : base(SampleSnapshot.Capture(song, default))
    {
    }

    // Starts again from the given song: a new document, or one just opened.
    public void Reset(Song song)
    {
        ResetTo(SampleSnapshot.Capture(song, default));
    }

    // Starts again from a song whose sample list has changed.
    public void Discard(Song song)
    {
        DiscardTo(SampleSnapshot.Capture(song, default));
    }

    // Takes the samples as they now stand as an undo step, an undo and a redo each putting the editor back where the state they restore was seen from; false where the samples did not change.
    public bool Record(Song song, SampleView viewBefore, SampleView viewAfter, object? source)
    {
        return RecordState(SampleSnapshot.Capture(song, viewAfter), Current.SeenThrough(viewBefore), source);
    }
}
