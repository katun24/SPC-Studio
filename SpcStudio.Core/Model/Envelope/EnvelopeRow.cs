namespace SpcStudio.Model;

// One row of a note's envelope: the tick it takes over at, counted from the note's own start, and the three bytes the DSP reads from there until the next row or the note's end.
public readonly record struct EnvelopeRow(int OffsetTicks, EnvelopeState State) : IOffsetRow<EnvelopeRow>
{
    // The same envelope taking over at another tick.
    public EnvelopeRow WithOffsetTicks(int offsetTicks)
    {
        return this with { OffsetTicks = offsetTicks };
    }
}
