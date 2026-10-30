namespace SpcStudio.Import;

// One group of AddmusicK's sample groups file: its name without the '#', and its entries in order.
public sealed record SampleGroup(string Name, IReadOnlyList<SampleGroupEntry> Entries)
{
    // Whether another group names the same samples in the same order, each one marked important alike.
    public bool HasSameEntriesAs(SampleGroup other)
    {
        return Entries.SequenceEqual(other.Entries);
    }
}
