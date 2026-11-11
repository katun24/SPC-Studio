using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// The rows of a list that follow one another with no gap - the envelope's, the channel volume's and the pan's: each row's Length first, how far it runs
// to the next row or to the note's end. Editing a Length moves the start of the row after it, or the last row's own start; '+' splits the last row in
// half, or adds a row behind an endless one; and a row taken off gives its length to the row before it, or the first row's to the row after it.
public abstract class SpanRows<TRow, THost> : EntryRows<RowSpan<TRow>, THost> where TRow : struct, IOffsetRow<TRow> where THost : IListHost<TRow>
{
    protected const int SpanLengthField = 0;
    private const int MinLengthTicks = 1;
    private const int LoneRowCount = 1;

    // One subject's rows as spans.
    protected override ImmutableArray<RowSpan<TRow>> ChainOf(THost subject)
    {
        return SpansOf(subject.Chain, subject);
    }

    // Rows on a subject as spans: each row's length to the next row, the last's to the end of the subject's room or none where it is endless.
    protected static ImmutableArray<RowSpan<TRow>> SpansOf(ImmutableArray<TRow> rows, THost subject)
    {
        var spans = ImmutableArray.CreateBuilder<RowSpan<TRow>>(rows.Length);
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            bool isEndless = rowIndex + 1 == rows.Length && !subject.HasOwnLength;
            int? lengthTicks = isEndless ? null : OffsetRows.LengthTicksAt(rows, rowIndex, subject.CapacityTicks);
            spans.Add(new RowSpan<TRow>(lengthTicks, rows[rowIndex]));
        }

        return spans.MoveToImmutable();
    }

    // Writes spans onto one subject as rows, answering whether it wrote them.
    protected override bool WriteChain(THost subject, ImmutableArray<RowSpan<TRow>> chain)
    {
        return subject.Write(RowsOf(chain));
    }

    // Spans cut at the end of a subject with a length of its own: the rows it does not reach taken off and the last running to its end.
    protected override ImmutableArray<RowSpan<TRow>> FittedTo(THost subject, ImmutableArray<RowSpan<TRow>> chain)
    {
        return SpansOf(OffsetRows.FitToNoteLength(RowsOf(chain), subject.CapacityTicks), subject);
    }

    // One subject's spans with the last split where it ends, at half the shortest row split with it, or with a row added behind it where it is endless,
    // or null where neither fits or there is no row to split.
    protected override ImmutableArray<RowSpan<TRow>>? WithRowAdded(THost subject, ImmutableArray<RowSpan<TRow>> chain, int? shortestSplitTicks)
    {
        if (chain.IsEmpty)
        {
            return null;
        }

        var lastSpan = chain[^1];
        var spansBefore = chain.RemoveAt(chain.Length - 1);
        if (SplitRowTicks(subject, chain) is not int lastLengthTicks)
        {
            if (RowsOf(chain)[^1].OffsetTicks + EndedEndlessRowTicks >= subject.CapacityTicks)
            {
                return null;
            }

            var (ending, endless) = SplitEndlessRow(lastSpan.Row, EndedEndlessRowTicks);
            return [.. spansBefore, new RowSpan<TRow>(EndedEndlessRowTicks, ending), new RowSpan<TRow>(null, endless)];
        }

        if (FirstHalfTicks(lastLengthTicks, shortestSplitTicks) is not int firstHalfTicks)
        {
            return null;
        }

        var (firstHalf, secondHalf) = SplitRow(lastSpan.Row, lastLengthTicks, firstHalfTicks);
        return [.. spansBefore, new RowSpan<TRow>(firstHalfTicks, firstHalf), new RowSpan<TRow>(lastLengthTicks - firstHalfTicks, secondHalf)];
    }

    // The ticks the last row runs, which is the row '+' splits, or null where there is none or it is endless.
    protected override int? SplitRowTicks(THost subject, ImmutableArray<RowSpan<TRow>> chain)
    {
        return chain.IsEmpty ? null : chain[^1].LengthTicks;
    }

    // The two rows a row running a number of ticks becomes when '+' splits it a number of ticks in: two copies of it, unless a kind of row says otherwise.
    protected virtual (TRow FirstHalf, TRow SecondHalf) SplitRow(TRow row, int lengthTicks, int firstHalfTicks)
    {
        return (row, row);
    }

    // The two rows an endless row becomes when '+' gives it an end a number of ticks in: two copies of it, unless a kind of row says otherwise.
    protected virtual (TRow Ending, TRow Endless) SplitEndlessRow(TRow row, int endingTicks)
    {
        return (row, row);
    }

    // A row's length, which an endless row has none of, or a field standing after it.
    protected override int FieldValue(RowSpan<TRow> entry, int field)
    {
        return field == SpanLengthField ? entry.LengthTicks.GetValueOrDefault() : RowFieldValue(entry.Row, field);
    }

    // What a field standing after the Length shows; there is none unless a kind of row has one.
    protected virtual int RowFieldValue(TRow row, int field)
    {
        return 0;
    }

    // A row's length, or the word an endless row shows in its place.
    protected override string FieldText(RowSpan<TRow> entry, int field)
    {
        return field == SpanLengthField && entry.LengthTicks is null ? EndlessLengthText : base.FieldText(entry, field);
    }

    // What a row's length can be: as long as it leaves the row it trades with a tick of its own - the row after it, or for the last row the row before
    // it - or, ahead of an endless row, as long as the subject's room allows; or what a field standing after it can hold.
    protected override ValueRange FieldRange(THost subject, ImmutableArray<RowSpan<TRow>> chain, int index, int field)
    {
        if (field != SpanLengthField)
        {
            return RowFieldRange(field);
        }

        int lengthTicks = FieldValue(chain[index], field);
        if (index + 1 < chain.Length)
        {
            int maxTicks = chain[index + 1].LengthTicks is int nextLengthTicks
                ? lengthTicks + nextLengthTicks - MinLengthTicks
                : subject.CapacityTicks - RowsOf(chain)[index].OffsetTicks - MinLengthTicks;
            return new(MinLengthTicks, maxTicks);
        }

        if (index == OffsetRows.FirstRowIndex)
        {
            return new(lengthTicks, lengthTicks);
        }

        return new(MinLengthTicks, lengthTicks + FieldValue(chain[index - 1], field) - MinLengthTicks);
    }

    // What a field standing after the Length can hold; there is none unless a kind of row has one.
    protected virtual ValueRange RowFieldRange(int field)
    {
        return new(0, 0);
    }

    // The spans with one row's length set, the row it trades with - the one after it, or for the last row the one before it - taking up the difference,
    // and unchanged for a row alone, which has none to trade with; or with a field standing after it set.
    protected override ImmutableArray<RowSpan<TRow>> WithFieldEdited(ImmutableArray<RowSpan<TRow>> chain, int index, int field, int value)
    {
        if (field != SpanLengthField)
        {
            return chain.SetItem(index, chain[index] with { Row = WithRowFieldEdited(chain[index].Row, field, value) });
        }

        if (IsRowAlone(chain.Length))
        {
            return chain;
        }

        int growthTicks = value - FieldValue(chain[index], field);
        var edited = chain.SetItem(index, chain[index] with { LengthTicks = value });
        int tradingIndex = index + 1 < chain.Length ? index + 1 : index - 1;
        if (edited[tradingIndex].LengthTicks is not int tradingLengthTicks)
        {
            return edited;
        }

        return edited.SetItem(tradingIndex, edited[tradingIndex] with { LengthTicks = tradingLengthTicks - growthTicks });
    }

    // A row with a field standing after the Length set; there is none unless a kind of row has one.
    protected virtual TRow WithRowFieldEdited(TRow row, int field, int value)
    {
        return row;
    }

    // The value a right-click puts a field back at: the even split of the two rows that trade length for a Length, and the kind of row's own answer for a field after it.
    protected override int FieldBaseValue(IReadOnlyList<THost> members, ImmutableArray<RowSpan<TRow>> chain, int index, int field)
    {
        return field == SpanLengthField ? EvenSplitTicks(chain, index) : ValueFieldBaseValue(ShownSubject(members), chain, index, field);
    }

    // The value a right-click puts a field standing after the Length back at; there is none unless a kind of row has one.
    protected override int ValueFieldBaseValue(THost subject, ImmutableArray<RowSpan<TRow>> chain, int index, int field)
    {
        return 0;
    }

    // A row's length is a tick count.
    protected override bool IsTickCountField(int field)
    {
        return field == SpanLengthField;
    }

    // An endless row's length cannot be edited.
    protected override bool IsFieldLocked(ImmutableArray<RowSpan<TRow>> chain, int index, int field)
    {
        return field == SpanLengthField && chain[index].LengthTicks is null;
    }

    // A row can be taken off wherever another stands to take over its length.
    protected override bool CanRemoveRow(ImmutableArray<RowSpan<TRow>> chain, int index)
    {
        return !IsRowAlone(chain.Length);
    }

    // The spans with one row taken off, the row before it running on over its length - or for the first row, the row after it starting where it did.
    protected override ImmutableArray<RowSpan<TRow>> WithRowRemoved(ImmutableArray<RowSpan<TRow>> chain, int index)
    {
        int heirIndex = index == OffsetRows.FirstRowIndex ? index + 1 : index - 1;
        var heir = chain[heirIndex];
        int? mergedLengthTicks = chain[index].LengthTicks is int removedLengthTicks ? heir.LengthTicks + removedLengthTicks : null;
        return chain.SetItem(heirIndex, heir with { LengthTicks = mergedLengthTicks }).RemoveAt(index);
    }

    // What the revert button leaves of the spans: the first row alone, which then runs to the end.
    protected override ImmutableArray<RowSpan<TRow>> RevertedChain(ImmutableArray<RowSpan<TRow>> chain)
    {
        return chain.IsEmpty ? chain : [chain[OffsetRows.FirstRowIndex] with { LengthTicks = null }];
    }

    // No count where there is one row, which is not shown as a list.
    protected override int? BannerRowCount(int rowCount)
    {
        return IsRowAlone(rowCount) ? null : rowCount;
    }

    // Whether there is one row alone, which nothing can take over from.
    protected static bool IsRowAlone(int rowCount)
    {
        return rowCount == LoneRowCount;
    }

    // The spans as rows, each taking over where the ones before it end.
    protected static ImmutableArray<TRow> RowsOf(ImmutableArray<RowSpan<TRow>> chain)
    {
        var rows = ImmutableArray.CreateBuilder<TRow>(chain.Length);
        int offsetTicks = OffsetRows.FirstOffsetTicks;
        foreach (var span in chain)
        {
            rows.Add(span.Row.WithOffsetTicks(offsetTicks));
            offsetTicks += span.LengthTicks.GetValueOrDefault();
        }

        return rows.MoveToImmutable();
    }

    // Half the length of a row and the row it trades with together, or a fixed length ahead of an endless row.
    private static int EvenSplitTicks(ImmutableArray<RowSpan<TRow>> chain, int index)
    {
        int lengthTicks = chain[index].LengthTicks.GetValueOrDefault();
        if (index + 1 < chain.Length)
        {
            return chain[index + 1].LengthTicks is int nextLengthTicks ? (lengthTicks + nextLengthTicks) / HalvesInASplit : EndedEndlessRowTicks;
        }

        if (index == OffsetRows.FirstRowIndex)
        {
            return lengthTicks;
        }

        return (lengthTicks + chain[index - 1].LengthTicks.GetValueOrDefault()) / HalvesInASplit;
    }
}
