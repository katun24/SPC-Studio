namespace SpcStudio.Model;

// Where any stretch of a note's chain sits in a saved file, a vibrato's, a tremolo's or a bend's: its delay, and how long it lasts or whether it runs on to the note's end.
internal abstract class SweepSpanDto
{
    public int Delay { get; set; }
    public int Length { get; set; }
    public bool IsEndless { get; set; }

    // An entry with a sweep's span written into it: its delay, its length and whether it runs on to the note's end.
    protected static TEntry WithSpanOf<TEntry, TSweep>(TEntry entry, TSweep sweep)
        where TEntry : SweepSpanDto
        where TSweep : struct, INoteSweep<TSweep>
    {
        entry.Delay = sweep.DelayTicks;
        entry.Length = sweep.LengthTicks;
        entry.IsEndless = sweep.IsEndless;
        return entry;
    }
}
