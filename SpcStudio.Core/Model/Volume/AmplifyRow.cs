namespace SpcStudio.Model;

// One row of a note's $FA $03 amplify: the tick it takes over at, counted from the note's own start, and the byte it holds from there until the next row or the note's end.
public readonly record struct AmplifyRow(int OffsetTicks, int Value) : IOffsetRow<AmplifyRow>
{
    // The same amplify taking over at another tick.
    public AmplifyRow WithOffsetTicks(int offsetTicks)
    {
        return this with { OffsetTicks = offsetTicks };
    }
}
