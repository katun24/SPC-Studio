namespace SpcStudio.Model;

// One note's $FA $03 amplify, carried on three rungs: the amplify lane as a note's own, and the merge every reader past the lane's drawing reads.
public static class NoteAmplify
{
    // The amplify lane as a note's own.
    public static readonly NoteFlatLane Lane = new(
        laneBase: (song, channel) => song.BaseAmplify(channel),
        laneDividers: (song, channel) => song.AmplifyChanges(channel),
        sourceOf: note => note.AmplifySource,
        ownValueOf: note => note.Amplify,
        instrumentValueOf: instrument => instrument?.Amplify ?? SnesVolume.DefaultAmplify,
        setNoteValue: (song, channel, startTick, source, value) => song.SetNoteAmplify(channel, startTick, source, value));
}
