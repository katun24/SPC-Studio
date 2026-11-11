namespace SpcStudio.Model;

// One row of a note's chain on a lane that fades: the tick it takes over at, counted from the note's own start, the state it sets there, and the byte it
// ramps to by the time the next row or the note's end comes, or none where it holds its state flat.
public readonly record struct NoteChainRegion<TState>(int OffsetTicks, TState State, int? FadeTo = null) : IOffsetRow<NoteChainRegion<TState>> where TState : struct
{
    // Whether the row ramps rather than holding its state.
    public bool Fades
    {
        get
        {
            return FadeTo is not null;
        }
    }

    // The same row taking over at another tick.
    public NoteChainRegion<TState> WithOffsetTicks(int offsetTicks)
    {
        return this with { OffsetTicks = offsetTicks };
    }
}
