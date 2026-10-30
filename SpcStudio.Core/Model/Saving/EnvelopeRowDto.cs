using System.Collections.Immutable;
using System.IO;

namespace SpcStudio.Model;

// One envelope row as both saved files write it, a note's or an instrument's: the tick it takes over at and the three bytes it holds.
internal sealed class EnvelopeRowDto
{
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
        ImmutableArray<EnvelopeRow> listedRows = [.. (rows ?? []).Select(row => RowOf(row, where))];
        if (!listedRows.IsEmpty && !OffsetRows.AreOrdered(listedRows))
        {
            throw new InvalidDataException($"{where} has envelope rows that do not start at the note's key-on and take over in order.");
        }

        return listedRows;
    }

    // A note's envelope rows read back, refusing as an instrument's are refused and also a row past the note's length.
    public static ImmutableArray<EnvelopeRow> NoteChainOf(List<EnvelopeRowDto>? rows, string where, int noteLengthTicks)
    {
        var chain = ChainOf(rows, where);
        if (!chain.IsEmpty && chain[^1].OffsetTicks >= noteLengthTicks)
        {
            throw new InvalidDataException($"{where} has an envelope row at tick {chain[^1].OffsetTicks}, past its length.");
        }

        return chain;
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
