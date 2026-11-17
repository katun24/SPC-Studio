namespace SpcStudio.Model;

// The song's undo and redo stacks, each state the whole of what an edit can change about the song but its samples.
public sealed class EditHistory : UndoHistory<SongSnapshot>
{
    // A history whose first state is the song as it stands.
    public EditHistory(Song song) : base(SongSnapshot.Capture(song))
    {
    }

    // Starts again from the given song: a new document, or one just opened.
    public void Reset(Song song)
    {
        ResetTo(SongSnapshot.Capture(song));
    }

    // Starts again from a song holding a change no step here can put back, the song being modified until it is saved.
    public void Discard(Song song)
    {
        DiscardTo(SongSnapshot.Capture(song));
    }

    // Takes the song as it now stands as an undo step, merging it into the last one where the same source made both; false where nothing changed.
    public bool Record(Song song, object? source)
    {
        return RecordState(SongSnapshot.Capture(song), Current, source);
    }
}
