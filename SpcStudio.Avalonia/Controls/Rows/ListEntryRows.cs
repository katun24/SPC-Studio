using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// A chain's rows where each row is one entry of the chain the subject holds, with nothing in front of them: the vibrato's, the tremolo's and the
// pitch bend's. The last entry's Length can say 'end', running on to the end of every note playing it.
public abstract class ListEntryRows<T, THost> : EntryRows<T, THost> where T : struct, INoteSweep<T>, IEquatable<T> where THost : IListHost<T>
{
    // The chain one subject's rows show.
    protected override ImmutableArray<T> ChainOf(THost subject)
    {
        return subject.Chain;
    }

    // Writes a chain onto one subject, answering whether it wrote it.
    protected override bool WriteChain(THost subject, ImmutableArray<T> chain)
    {
        return subject.Write(chain);
    }

    // A chain cut at the end of a subject with a length of its own, as a note holds its vibrato or tremolo.
    protected override ImmutableArray<T> FittedTo(THost subject, ImmutableArray<T> chain)
    {
        return NoteSweep.FitToNoteLength(chain, subject.CapacityTicks);
    }

    // Whether two chains play the same on one subject, a last entry ending exactly at a note's end being the same whether it says 'end' or not.
    protected override bool PlaysSame(THost subject, ImmutableArray<T> first, ImmutableArray<T> second)
    {
        return subject.HasOwnLength ? NoteSweep.PlaySameOnNote(first, second, subject.CapacityTicks) : base.PlaysSame(subject, first, second);
    }

    // Whether the last entry of a chain can run on to the end of every subject, which every entry can unless a kind of chain says otherwise.
    protected virtual bool CanRunToEnd(IReadOnlyList<THost> members, ImmutableArray<T> chain)
    {
        return true;
    }

    // The ticks the endless last entry runs on a note, which is the entry '+' splits, or null where there is none or the subject has no length of its own.
    protected override int? SplitRowTicks(THost subject, ImmutableArray<T> chain)
    {
        return subject.HasOwnLength && HasEndlessTail(chain) ? chain[^1].LengthTicks : null;
    }

    // Whether a chain's last entry says 'end'.
    private static bool HasEndlessTail(ImmutableArray<T> chain)
    {
        return !chain.IsEmpty && chain[^1].IsEndless;
    }

    // One subject's chain with its endless last entry given an end, to make room behind it: half of what it runs on a note, or of the shortest entry split with it, or a fixed length on a subject with
    // no length of its own - the chain as it is where its last entry already ends, and null where the entry is too short to halve.
    protected ImmutableArray<T>? WithEndlessTailEnded(THost subject, ImmutableArray<T> chain, int? shortestSplitTicks)
    {
        if (!HasEndlessTail(chain))
        {
            return chain;
        }

        var lastEntry = chain[^1];
        if (SplitRowTicks(subject, chain) is not int lastLengthTicks)
        {
            return chain.SetItem(chain.Length - 1, NoteSweep.WithFixedLength(lastEntry, EndedEndlessRowTicks));
        }

        if (FirstHalfTicks(lastLengthTicks, shortestSplitTicks) is not int firstHalfTicks)
        {
            return null;
        }

        return chain.SetItem(chain.Length - 1, NoteSweep.WithFixedLength(lastEntry, firstHalfTicks));
    }

    // An entry's Length, or the word an endless entry shows in its place.
    protected override string FieldText(T entry, int field)
    {
        return field == LengthField && entry.IsEndless ? EndlessLengthText : base.FieldText(entry, field);
    }

    // The value an arrow or the wheel steps a field to: an endless Length to the length an endless row is given, and any other field one step on.
    protected override int SteppedValue(T entry, int field, int step)
    {
        return field == LengthField && entry.IsEndless ? EndedEndlessRowTicks : base.SteppedValue(entry, field, step);
    }

    // The chain a right-click on the last entry's Length leaves: that entry running on to the end where it can, and the chain unchanged where it
    // cannot; null for every other field, which goes back to its base value.
    protected override ImmutableArray<T>? WithFieldReset(IReadOnlyList<THost> members, ImmutableArray<T> chain, int index, int field)
    {
        if (field != LengthField || index != chain.Length - 1)
        {
            return null;
        }

        return CanRunToEnd(members, chain) ? chain.SetItem(index, chain[index].WithEndless(true)) : chain;
    }

    // The chain with one entry taken off, the entry in front of it running on to the end in its place where it was the endless last one.
    protected override ImmutableArray<T> WithRowRemoved(ImmutableArray<T> chain, int index)
    {
        var remaining = chain.RemoveAt(index);
        if (!chain[index].IsEndless || remaining.IsEmpty || index != remaining.Length)
        {
            return remaining;
        }

        return remaining.SetItem(remaining.Length - 1, remaining[^1].WithEndless(true));
    }
}
