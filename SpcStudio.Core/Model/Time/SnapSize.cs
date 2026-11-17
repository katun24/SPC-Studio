namespace SpcStudio.Model;

// A step the roll's edits move by: placing a note, dragging or resizing one, dragging either marker and moving the playback position all move by whole units of the selected size.
public sealed record SnapSize(string Label, int Ticks)
{
    private const int TripletsPerBinaryLength = 3;

    // Every size, coarse to fine, a T being the triplet of the length beside it.
    public static readonly IReadOnlyList<SnapSize> Choices =
    [
        new("1/1 (192)", SnesTiming.TicksPerWholeNote),
        new("1/2 (96)", SnesTiming.TicksPerHalfNote),
        new("1/2T (64)", SnesTiming.TicksPerWholeNote / TripletsPerBinaryLength),
        new("1/4 (48)", SnesTiming.TicksPerQuarterNote),
        new("1/4T (32)", SnesTiming.TicksPerHalfNote / TripletsPerBinaryLength),
        new("1/8 (24)", SnesTiming.TicksPerEighthNote),
        new("1/8T (16)", SnesTiming.TicksPerQuarterNote / TripletsPerBinaryLength),
        new("1/16 (12)", SnesTiming.TicksPerSixteenthNote),
        new("1/16T (8)", SnesTiming.TicksPerEighthNote / TripletsPerBinaryLength),
        new("1/32 (6)", SnesTiming.TicksPerSixteenthNote / 2),
        new("1/32T (4)", SnesTiming.TicksPerSixteenthNote / TripletsPerBinaryLength),
        new("1/64 (3)", SnesTiming.TicksPerSixteenthNote / 4),
        new("2 ticks", 2),
        new("1 tick", 1),
    ];

    // What a new song snaps to: a sixteenth.
    public static readonly SnapSize Default = Choices.First(choice => choice.Ticks == SnesTiming.TicksPerSixteenthNote);

    // Whether a tick count is one of the sizes.
    public static bool IsValid(int ticks)
    {
        return Choices.Any(choice => choice.Ticks == ticks);
    }

    // The size with this many ticks, or the default where there is none.
    public static SnapSize ForTicks(int ticks)
    {
        return Choices.FirstOrDefault(choice => choice.Ticks == ticks) ?? Default;
    }

    // The size as the drop-down shows it.
    public override string ToString()
    {
        return Label;
    }
}
