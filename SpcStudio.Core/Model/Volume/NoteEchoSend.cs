namespace SpcStudio.Model;

// One note's echo send - whether it is fed into the echo buffer - carried on three rungs: the Ch Echo lane's bit as a note's own, as 0 and 1.
public static class NoteEchoSend
{
    // The Ch Echo lane as a note's own.
    public static readonly NoteFlatLane Lane = new(
        laneBase: (song, channel) => song.IsBaseChannelEchoOn(channel) ? 1 : 0,
        laneDividers: (song, channel) => song.ChannelEchoChanges(channel),
        sourceOf: note => note.EchoSource,
        ownValueOf: note => note.IsEchoOn ? 1 : 0,
        instrumentValueOf: instrument => (instrument?.IsEchoOn ?? SnesEcho.IsChannelEchoOnByDefault) ? 1 : 0,
        setNoteValue: (song, channel, startTick, source, value) => song.SetNoteEcho(channel, startTick, source, value != 0));

    // The channel's echo send as the lane and its notes together say it.
    public static (bool Base, IReadOnlyList<LaneValueChange> Changes) Merged(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var (mergedBase, mergedChanges) = Lane.Merged(song, channel, notes);
        return (mergedBase != 0, mergedChanges);
    }

    // Whether the note is fed into the echo buffer, whichever rung decides it.
    public static bool IsEchoOn(Song song, int channel, Note note)
    {
        return Lane.ValuePlayedBy(song, channel, note) != 0;
    }

    // Gives the note an echo send of its own.
    public static void ApplyOverride(Song song, int channel, Note note, bool isEchoOn)
    {
        Lane.ApplyOverride(song, channel, note, isEchoOn ? 1 : 0);
    }
}
