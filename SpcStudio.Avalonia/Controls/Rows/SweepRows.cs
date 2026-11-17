using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// A sweep chain's rows - the vibrato's or the tremolo's, the note panel's or the instrument panel's: one row per stretch, each a Delay, a Length and the sweep's own values.
public sealed class SweepRows<T> : ListEntryRows<T, IListHost<T>> where T : struct, INoteSweep<T>, IEquatable<T>
{
    // A row's fields, in the order they stand.
    public required IReadOnlyList<SweepField<T>> Fields { get; init; }

    // A fresh stretch at a delay and a length, given the stretch in front of it where there is one.
    public required Func<T?, int, int, T> FreshStretch { get; init; }

    // The fields' labels, in the order they stand.
    protected override IReadOnlyList<string> FieldLabels
    {
        get
        {
            return [.. Fields.Select(field => field.Label)];
        }
    }

    // One subject's chain with one more stretch on the end, starting where the chain ends and running on to the end, the endless stretch in front of it given an end first, or null where none fits.
    protected override ImmutableArray<T>? WithRowAdded(IListHost<T> subject, ImmutableArray<T> chain, int? shortestSplitTicks)
    {
        if (WithEndlessTailEnded(subject, chain, shortestSplitTicks) is not { } endedChain)
        {
            return null;
        }

        int roomTicks = subject.CapacityTicks - NoteSweep.SpanBefore(endedChain, endedChain.Length);
        if (roomTicks < NoteSweep.MinLengthTicks)
        {
            return null;
        }

        T? previousStretch = endedChain.IsEmpty ? null : endedChain[^1];
        return [.. endedChain, FreshStretch(previousStretch, NoteSweep.MinDelayTicks, roomTicks).WithEndless(true)];
    }

    // What one field of a stretch shows.
    protected override int FieldValue(T entry, int field)
    {
        return Fields[field].ValueOf(entry);
    }

    // What one field of a stretch can hold on one subject, given the chain it is in and the subject's capacity.
    protected override ValueRange FieldRange(IListHost<T> subject, ImmutableArray<T> chain, int index, int field)
    {
        return Fields[field].RangeIn(chain, index, subject.CapacityTicks);
    }

    // The chain with one field of one stretch set.
    protected override ImmutableArray<T> WithFieldEdited(ImmutableArray<T> chain, int index, int field, int value)
    {
        return chain.SetItem(index, Fields[field].WithValue(chain[index], value));
    }

    // The value one of a stretch's own fields goes back to, which is what a fresh stretch in its place would hold.
    protected override int ValueFieldBaseValue(IListHost<T> subject, ImmutableArray<T> chain, int index, int field)
    {
        T? previousStretch = index == 0 ? null : chain[index - 1];
        return Fields[field].ValueOf(FreshStretch(previousStretch, NoteSweep.MinDelayTicks, NoteSweep.MinLengthTicks));
    }

    // Whether a field is a tick count.
    protected override bool IsTickCountField(int field)
    {
        return Fields[field].IsTickCount;
    }

    // Whether the chain leaves one field of a stretch nothing to hold.
    protected override bool IsFieldLocked(ImmutableArray<T> chain, int index, int field)
    {
        return Fields[field].IsLocked?.Invoke(chain, index) == true;
    }
}
