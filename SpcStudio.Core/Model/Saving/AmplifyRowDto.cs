using System.Collections.Immutable;

namespace SpcStudio.Model;

// One $FA $03 amplify row as both saved files write it, a note's or an instrument's: the tick it takes over at and the byte it holds.
internal sealed class AmplifyRowDto
{
    private const string RowsName = "amplify";

    public int Offset { get; set; }
    public int Value { get; set; }

    // Amplify rows as they are written, or null where there are none.
    public static List<AmplifyRowDto>? ListOf(IReadOnlyCollection<AmplifyRow> rows)
    {
        return SavedFileJson.EntriesOrNull(rows, row => new AmplifyRowDto { Offset = row.OffsetTicks, Value = row.Value });
    }

    // Amplify rows read back in file order, refusing a value out of range, a first row off the key-on or rows out of order.
    public static ImmutableArray<AmplifyRow> ChainOf(List<AmplifyRowDto>? rows, string where)
    {
        return OffsetRowChains.ChainOf(rows, where, RowsName, RowOf);
    }

    // A note's amplify rows read back, refusing as an instrument's are refused and also a row past the note's length.
    public static ImmutableArray<AmplifyRow> NoteChainOf(List<AmplifyRowDto>? rows, string where, int noteLengthTicks)
    {
        return OffsetRowChains.NoteChainOf(rows, where, RowsName, RowOf, noteLengthTicks);
    }

    // One row read back, refusing a value out of range.
    private static AmplifyRow RowOf(AmplifyRowDto row, string where)
    {
        SavedFileChecks.RequireAmplify(where, row.Value);
        return new AmplifyRow(row.Offset, row.Value);
    }
}
