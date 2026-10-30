namespace SpcStudio.Model;

// A named note length, offered by the toolbar's drop-down.
public sealed record NoteLength(string Label, int Ticks)
{
    public static readonly NoteLength Whole = new("Whole", SnesTiming.TicksPerWholeNote);
    public static readonly NoteLength Half = new("1/2", SnesTiming.TicksPerHalfNote);
    public static readonly NoteLength Quarter = new("1/4", SnesTiming.TicksPerQuarterNote);
    public static readonly NoteLength Eighth = new("1/8", SnesTiming.TicksPerEighthNote);
    public static readonly NoteLength Sixteenth = new("1/16", SnesTiming.TicksPerSixteenthNote);
    public static readonly IReadOnlyList<NoteLength> Choices = [Whole, Half, Quarter, Eighth, Sixteenth];
    public static readonly NoteLength Default = Eighth;

    // The length as the drop-down shows it.
    public override string ToString()
    {
        return Label;
    }
}
