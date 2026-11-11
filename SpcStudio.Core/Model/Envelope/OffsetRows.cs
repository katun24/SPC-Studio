using System.Collections.Immutable;

namespace SpcStudio.Model;

// The rules every list of a note's rows that follow one another with no gap shares - the envelope's, the channel volume's and the pan's: the first at
// the note's start, each later one taking over where the one before it ends and the last running to the note's end.
public static class OffsetRows
{
    public const int FirstOffsetTicks = 0;
    public const int FirstRowIndex = 0;

    // The rows a note of a length can play: every row taking over before its end, the first always kept.
    public static ImmutableArray<TRow> FitToNoteLength<TRow>(ImmutableArray<TRow> rows, int noteLengthTicks) where TRow : struct, IOffsetRow<TRow>
    {
        if (rows.IsDefaultOrEmpty)
        {
            return [];
        }

        int keptCount = 1;
        while (keptCount < rows.Length && rows[keptCount].OffsetTicks < noteLengthTicks)
        {
            keptCount++;
        }

        return keptCount == rows.Length ? rows : [.. rows.Take(keptCount)];
    }

    // Which row is in force a number of ticks after the note's start: the last to take over at or before it.
    public static int InForceIndexAt<TRow>(ImmutableArray<TRow> rows, int offsetTicks) where TRow : struct, IOffsetRow<TRow>
    {
        int inForceIndex = 0;
        while (inForceIndex + 1 < rows.Length && rows[inForceIndex + 1].OffsetTicks <= offsetTicks)
        {
            inForceIndex++;
        }

        return inForceIndex;
    }

    // The row in force a number of ticks after the note's start.
    public static TRow InForceAt<TRow>(ImmutableArray<TRow> rows, int offsetTicks) where TRow : struct, IOffsetRow<TRow>
    {
        return rows[InForceIndexAt(rows, offsetTicks)];
    }

    // Rows that each hold one state flat, rebased onto a note trimmed at its head: the row in force at the new start becomes the first as it is, and
    // every row after it keeps its tick.
    public static ImmutableArray<TRow> TrimHeadOfFlatRows<TRow>(ImmutableArray<TRow> rows, int trimmedTicks) where TRow : struct, IOffsetRow<TRow>
    {
        return TrimHead(rows, trimmedTicks, inForceIndex => rows[inForceIndex]);
    }

    // The rows a note plays: its own where it holds any, and otherwise the first of the rows its instrument gives it alone.
    public static ImmutableArray<TRow> OwnOrFirstOf<TRow>(ImmutableArray<TRow> ownRows, ImmutableArray<TRow> instrumentRows) where TRow : struct, IOffsetRow<TRow>
    {
        return ownRows.IsEmpty ? [instrumentRows[FirstRowIndex]] : ownRows;
    }

    // The rows a note plays by its rung: its instrument's cut to its length on the instrument's, and otherwise its own, or the instrument's first alone
    // where it holds none.
    public static ImmutableArray<TRow> PlayedOnRung<TRow>(NoteSource source, ImmutableArray<TRow> ownRows, ImmutableArray<TRow> instrumentRows, int noteLengthTicks)
        where TRow : struct, IOffsetRow<TRow>
    {
        return source == NoteSource.Instrument ? FitToNoteLength(instrumentRows, noteLengthTicks) : OwnOrFirstOf(ownRows, instrumentRows);
    }

    // The rows each of a channel's notes plays under a state that moves over the song: the state at its start, then one at every tick strictly inside
    // it where the state moves, given the ticks at which it can move in order.
    public static List<ImmutableArray<TRow>> PlayedOverTimeline<TRow, TState>(
        IReadOnlyList<Note> notes, IReadOnlyList<int> changeTicks, Func<int, TState> stateAt, Func<int, TState, TRow> rowAt, Func<TRow, TState> stateOf)
        where TRow : struct, IOffsetRow<TRow>
    {
        var rowsOfEach = new List<ImmutableArray<TRow>>(notes.Count);
        foreach (var note in notes)
        {
            var rows = ImmutableArray.CreateBuilder<TRow>();
            rows.Add(rowAt(FirstOffsetTicks, stateAt(note.StartTick)));

            // The first state change after the note's start.
            int changeIndex = SortedSearch.CountLeading(changeTicks, tick => tick <= note.StartTick);
            for (; changeIndex < changeTicks.Count && changeTicks[changeIndex] < note.EndTick(); changeIndex++)
            {
                var state = stateAt(changeTicks[changeIndex]);
                if (!EqualityComparer<TState>.Default.Equals(state, stateOf(rows[^1])))
                {
                    rows.Add(rowAt(changeTicks[changeIndex] - note.StartTick, state));
                }
            }

            rowsOfEach.Add(rows.ToImmutable());
        }

        return rowsOfEach;
    }

    // The rows rebased onto a note trimmed at its head: the row in force at the new start rebuilt as the first, and every row after it keeping its tick.
    public static ImmutableArray<TRow> TrimHead<TRow>(ImmutableArray<TRow> rows, int trimmedTicks, Func<int, TRow> firstRowFromInForceIndex) where TRow : struct, IOffsetRow<TRow>
    {
        if (trimmedTicks <= 0 || rows.IsDefaultOrEmpty)
        {
            return rows;
        }

        int inForceIndex = InForceIndexAt(rows, trimmedTicks);
        var firstRow = firstRowFromInForceIndex(inForceIndex).WithOffsetTicks(FirstOffsetTicks);
        return [firstRow, .. rows.Skip(inForceIndex + 1).Select(row => row.WithOffsetTicks(row.OffsetTicks - trimmedTicks))];
    }

    // How long the row at an index runs: to the next row, or to the end of a note of a length for the last.
    public static int LengthTicksAt<TRow>(ImmutableArray<TRow> rows, int rowIndex, int noteLengthTicks) where TRow : struct, IOffsetRow<TRow>
    {
        int endTicks = rowIndex + 1 < rows.Length ? rows[rowIndex + 1].OffsetTicks : noteLengthTicks;
        return endTicks - rows[rowIndex].OffsetTicks;
    }

    // Whether rows can stand on a note as they are: the first at the note's start, the rest taking over in order, none on the tick of the one before it.
    public static bool AreOrdered<TRow>(ImmutableArray<TRow> rows) where TRow : struct, IOffsetRow<TRow>
    {
        if (rows.IsDefaultOrEmpty || rows[0].OffsetTicks != FirstOffsetTicks)
        {
            return false;
        }

        for (int rowIndex = 1; rowIndex < rows.Length; rowIndex++)
        {
            if (rows[rowIndex].OffsetTicks <= rows[rowIndex - 1].OffsetTicks)
            {
                return false;
            }
        }

        return true;
    }
}
