namespace SpcStudio.Model;

// A lane a note can stand over on three rungs - the lane, its instrument or its own value - read and moved by channel and note.
public interface ILaneRungs
{
    // Which rung the note as the song holds it stands on.
    public LaneSource LiveSourceOf(Song song, int channel, Note note);

    // Moves a note to a rung without changing what it plays.
    public void SetSource(Song song, int channel, Note note, LaneSource source);
}
