namespace SpcStudio.Model;

// What a drag, a copy, a paste and a delete need of a lane a note can override: read the note's own value off it, write one, and take one away.
public interface INoteOverride<TState> where TState : struct
{
    // The note's own value where it differs from what the channel would be at without it, and null where the note overrides nothing.
    public TState? OverrideOf(Song song, int channel, Note note);

    // Puts the note at a value of its own.
    public void ApplyOverride(Song song, int channel, Note note, TState state);

    // Takes the note's override away, leaving everything after it where it was.
    public void ClearOverride(Song song, int channel, Note note);
}
