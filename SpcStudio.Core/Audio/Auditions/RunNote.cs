namespace SpcStudio.Audio;

// One note of the note panel's run: what it keys on with, how many ticks after the run's first note it starts, and whether the note before it in the run binds into it.
public sealed record RunNote(AuditionNote Note, int OffsetTicks, bool IsBoundFromPrevious)
{
    // The lanes the note walks, which a note of a run has to have.
    public AuditionLanes Lanes
    {
        get
        {
            return Note.Lanes ?? throw new InvalidOperationException("A note of a run needs lanes to walk.");
        }
    }
}
