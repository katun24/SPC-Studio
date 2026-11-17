using System.Collections.Immutable;
using System.IO;

namespace SpcStudio.Model;

// The checks every list of saved rows that follow one another with no gap takes on the way back in - the envelope's and the amplify's.
internal static class OffsetRowChains
{
    // Rows read back in file order through a reader of one row, refusing rows that do not start at the note's key-on and take over in order.
    public static ImmutableArray<TRow> ChainOf<TEntry, TRow>(List<TEntry>? entries, string where, string rowsName, Func<TEntry, string, TRow> rowOf)
        where TRow : struct, IOffsetRow<TRow>
    {
        ImmutableArray<TRow> listedRows = [.. (entries ?? []).Select(entry => rowOf(entry, where))];
        if (!listedRows.IsEmpty && !OffsetRows.AreOrdered(listedRows))
        {
            throw new InvalidDataException($"{where} has {rowsName} rows that do not start at the note's key-on and take over in order.");
        }

        return listedRows;
    }

    // A note's rows read back, refusing as an instrument's are refused and also a row past the note's length.
    public static ImmutableArray<TRow> NoteChainOf<TEntry, TRow>(List<TEntry>? entries, string where, string rowsName, Func<TEntry, string, TRow> rowOf, int noteLengthTicks)
        where TRow : struct, IOffsetRow<TRow>
    {
        var chain = ChainOf(entries, where, rowsName, rowOf);
        if (!chain.IsEmpty && chain[^1].OffsetTicks >= noteLengthTicks)
        {
            throw new InvalidDataException($"{where} has an {rowsName} row at tick {chain[^1].OffsetTicks}, past its length.");
        }

        return chain;
    }
}
