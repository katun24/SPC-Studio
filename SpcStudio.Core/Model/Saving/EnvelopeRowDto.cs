using System.Collections.Immutable;

namespace SpcStudio.Model;

// One envelope row as both saved files write it, a note's or an instrument's: the tick it takes over at and the three bytes it holds.
internal sealed class EnvelopeRowDto
{
    private const string RowsName = "envelope";

    public int Offset { get; set; }
    public int Adsr1 { get; set; }
    public int Adsr2 { get; set; }
    public int Gain { get; set; }

    // Envelope rows as they are written, or null where there are none.
    public static List<EnvelopeRowDto>? ListOf(ImmutableArray<EnvelopeRow> rows)
    {
        return SavedFileJson.EntriesOrNull(rows, row => new EnvelopeRowDto { Offset = row.OffsetTicks, Adsr1 = row.State.Adsr1, Adsr2 = row.State.Adsr2, Gain = row.State.Gain });
    }

    // Envelope rows read back in file order, refusing a byte out of range, a first row off the key-on or rows out of order.
    public static ImmutableArray<EnvelopeRow> ChainOf(List<EnvelopeRowDto>? rows, string where)
    {
        return OffsetRowChains.ChainOf(rows, where, RowsName, RowOf);
    }

    // A note's envelope rows read back, refusing as an instrument's are refused and also a row past the note's length.
    public static ImmutableArray<EnvelopeRow> NoteChainOf(List<EnvelopeRowDto>? rows, string where, int noteLengthTicks)
    {
        return OffsetRowChains.NoteChainOf(rows, where, RowsName, RowOf, noteLengthTicks);
    }

    // One row read back, refusing a byte out of range.
    private static EnvelopeRow RowOf(EnvelopeRowDto row, string where)
    {
        SavedFileChecks.RequireInRange(where, "envelope ADSR1", row.Adsr1, SnesEnvelopeOverride.ByteRange);
        SavedFileChecks.RequireInRange(where, "envelope ADSR2", row.Adsr2, SnesEnvelopeOverride.ByteRange);
        SavedFileChecks.RequireInRange(where, "envelope GAIN", row.Gain, SnesEnvelopeOverride.ByteRange);
        return new EnvelopeRow(row.Offset, new EnvelopeState(row.Adsr1, row.Adsr2, row.Gain));
    }
}
