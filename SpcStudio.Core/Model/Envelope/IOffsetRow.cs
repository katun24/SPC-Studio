namespace SpcStudio.Model;

// One row of a note's rows that follow one another with no gap: the tick it takes over at, counted from the note's own start, and the same row moved to another tick.
public interface IOffsetRow<TRow> where TRow : struct, IOffsetRow<TRow>
{
    public int OffsetTicks { get; }

    // The same row taking over at another tick.
    public TRow WithOffsetTicks(int offsetTicks);
}
