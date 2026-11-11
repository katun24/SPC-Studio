using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// A pitch bend chain's rows - the note panel's or the instrument panel's: one row per bend, each a Delay, a Length and an Offset.
public sealed class PitchBendRows : ListEntryRows<PitchBend, IBendHost>
{
    private const int OffsetField = 2;
    private const int FreshOffsetSemitones = 1;
    private const int LevelOffsetSemitones = 0;
    private const string OffsetLabel = "Offset";

    // Gives the section's buttons the pitch bend's tooltips.
    public PitchBendRows()
    {
        SectionTooltips = new RowSectionTooltips(Tooltips.AddPitchBendRow, Tooltips.ClearPitchBends);
    }

    protected override IReadOnlyList<string> FieldLabels { get; } = [FieldRows.DelayLabel, FieldRows.LengthLabel, OffsetLabel];

    // The offsets the bend at an index can hold on a note at a pitch: those that keep every pitch the note reaches inside the roll.
    public static ValueRange OffsetRangeOn(ImmutableArray<PitchBend> chain, int index, int semitone)
    {
        int startSemitone = semitone + PitchBend.OffsetBefore(chain, index);
        var (lowestBehind, highestBehind) = PitchBend.Reach(chain.Skip(index + 1));
        return Song.SemitoneRange.HoldingReach(startSemitone + lowestBehind, startSemitone + highestBehind);
    }

    // The offsets the bend at an index can hold on a chain with no pitch of its own: those that keep the whole chain no wider than the roll.
    public static ValueRange OffsetRangeAnywhere(ImmutableArray<PitchBend> chain, int index)
    {
        int startOffset = PitchBend.OffsetBefore(chain, index);
        var (lowestBefore, highestBefore) = PitchBend.Reach(chain.Take(index));
        var (lowestBehind, highestBehind) = PitchBend.Reach(chain.Skip(index + 1));
        int span = PitchBend.MaxReachSemitones;
        return new(highestBefore - span - startOffset - lowestBehind, span + lowestBefore - startOffset - highestBehind);
    }

    // One subject's chain with one more bend on the end, starting where the chain reaches, its ramp running on to the end, and moving a fresh bend's
    // semitone - the endless bend in front of it given an end first - or null where none fits.
    protected override ImmutableArray<PitchBend>? WithRowAdded(IBendHost subject, ImmutableArray<PitchBend> chain, int? shortestSplitTicks)
    {
        if (WithEndlessTailEnded(subject, chain, shortestSplitTicks) is not { } endedChain)
        {
            return null;
        }

        int roomTicks = subject.CapacityTicks - ReachedTicks(endedChain);
        if (roomTicks < PitchBend.MinLengthTicks)
        {
            return null;
        }

        int lengthTicks = Math.Min(roomTicks, PitchBend.MaxLengthTicks);
        return endedChain.Add(new PitchBend(PitchBend.MinDelayTicks, lengthTicks, FreshOffsetIn(subject.OffsetRange(endedChain, endedChain.Length))) { IsEndless = true });
    }

    // What one field of a bend shows.
    protected override int FieldValue(PitchBend bend, int field)
    {
        return field switch
        {
            DelayField => bend.DelayTicks,
            LengthField => bend.LengthTicks,
            _ => bend.OffsetSemitones,
        };
    }

    // What one field of a bend can hold on one subject: a delay that still starts the ramp inside its capacity, a length short of the joint behind it, and the subject's offsets.
    protected override ValueRange FieldRange(IBendHost subject, ImmutableArray<PitchBend> chain, int index, int field)
    {
        return field switch
        {
            DelayField => new(PitchBend.MinDelayTicks, Math.Max(PitchBend.MinDelayTicks, PitchBend.MaxDelayAt(chain, index, subject.CapacityTicks))),
            LengthField => new(PitchBend.MinLengthTicks, MaxLengthTicks(subject, chain, index)),
            _ => subject.OffsetRange(chain, index),
        };
    }

    // A chain cut at the end of a subject with a length of its own, as a note holds its bends.
    protected override ImmutableArray<PitchBend> FittedTo(IBendHost subject, ImmutableArray<PitchBend> chain)
    {
        return PitchBend.FitToNoteLength(chain, subject.CapacityTicks);
    }

    // The chain with one field of one bend set, a length moving the joint at the ramp's end.
    protected override ImmutableArray<PitchBend> WithFieldEdited(ImmutableArray<PitchBend> chain, int index, int field, int value)
    {
        var bend = chain[index];
        return field switch
        {
            DelayField => chain.SetItem(index, bend with { DelayTicks = value }),
            LengthField => WithLengthEdited(chain, index, value),
            _ => chain.SetItem(index, bend with { OffsetSemitones = value }),
        };
    }

    // The offset a bend goes back to, which is a fresh bend's.
    protected override int ValueFieldBaseValue(IBendHost subject, ImmutableArray<PitchBend> chain, int index, int field)
    {
        return FreshOffsetIn(subject.OffsetRange(chain, index));
    }

    // Whether a value is the offset of a bend that would move no pitch, which no bend may hold.
    protected override bool IsValueRefused(int field, int value)
    {
        return field == OffsetField && value == LevelOffsetSemitones;
    }

    // Whether a field is one of the two tick counts.
    protected override bool IsTickCountField(int field)
    {
        return field != OffsetField;
    }

    // Whether the last bend's ramp can run on to the end of every subject: always on one with no length of its own, and on a note only where the length byte can say it.
    protected override bool CanRunToEnd(IReadOnlyList<IBendHost> members, ImmutableArray<PitchBend> chain)
    {
        return members.All(member => !member.HasOwnLength || PitchBend.CanRunToNoteEnd(chain, chain.Length - 1, member.CapacityTicks));
    }

    // Whether a field is the offset, which takes a sign.
    protected override bool IsSignedField(int field)
    {
        return field == OffsetField;
    }

    // A fresh bend's offset inside a range: a semitone up, or a semitone down where the range does not reach up that far.
    private static int FreshOffsetIn(ValueRange range)
    {
        return range.Max >= FreshOffsetSemitones ? FreshOffsetSemitones : -FreshOffsetSemitones;
    }

    // Ticks from the chain's start to where its last ramp ends.
    private static int ReachedTicks(ImmutableArray<PitchBend> chain)
    {
        return NoteSweep.SpanBefore(chain, chain.Length);
    }

    // The chain with one bend's ramp set to a length, the segment behind it giving up exactly the ticks the ramp took.
    private static ImmutableArray<PitchBend> WithLengthEdited(ImmutableArray<PitchBend> chain, int index, int lengthTicks)
    {
        var bend = chain[index];
        var lengthened = chain.SetItem(index, NoteSweep.WithFixedLength(bend, lengthTicks));
        if (index >= lengthened.Length - 1)
        {
            return lengthened;
        }

        int takenTicks = lengthTicks - bend.LengthTicks;
        var next = lengthened[index + 1];
        if (next.DelayTicks > PitchBend.MinDelayTicks)
        {
            return lengthened.SetItem(index + 1, next with { DelayTicks = next.DelayTicks - takenTicks });
        }

        return lengthened.SetItem(index + 1, next with { LengthTicks = next.LengthTicks - takenTicks });
    }

    // The longest ramp a bend can be given: one tick short of the joint behind its end, or the length byte's own cap on the chain's last bend and ahead of an
    // endless ramp on a subject with no length of its own, which has no end to be short of.
    private static int MaxLengthTicks(IBendHost subject, ImmutableArray<PitchBend> chain, int index)
    {
        if (index >= chain.Length - 1)
        {
            return PitchBend.MaxLengthTicks;
        }

        var next = chain[index + 1];
        if (next.DelayTicks == PitchBend.MinDelayTicks && next.IsEndless && !subject.HasOwnLength)
        {
            return PitchBend.MaxLengthTicks;
        }

        int ticksBehind = next.DelayTicks > PitchBend.MinDelayTicks ? next.DelayTicks : next.LengthTicks;
        return Math.Min(chain[index].LengthTicks + ticksBehind - 1, PitchBend.MaxLengthTicks);
    }
}
