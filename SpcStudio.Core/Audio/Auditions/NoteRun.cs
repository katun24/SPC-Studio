namespace SpcStudio.Audio;

// The note panel's audition: a stretch of one channel's notes keyed on in turn on one voice, once or over and over - the walk the voice follows, the
// notes, whether it starts over at its end, how many ticks of the pass the driver has run, and which note keys on next.
internal sealed class NoteRun(AuditionWalk walk, IReadOnlyList<RunNote> notes, bool isLooping)
{
    public AuditionWalk Walk { get; } = walk;
    public IReadOnlyList<RunNote> Notes { get; set; } = notes;
    public bool IsLooping { get; } = isLooping;
    public int TicksSounded { get; set; }
    public int NextNoteIndex { get; set; }

    // How many ticks one pass lasts: from the first note's start to the last note's end.
    public int LengthTicks
    {
        get
        {
            var lastNote = Notes[^1];
            return lastNote.OffsetTicks + lastNote.Lanes.EndTick - lastNote.Lanes.StartTick;
        }
    }
}
