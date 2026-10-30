using System.Collections.Immutable;

namespace SpcStudio.Model;

// A note's chain of sweeps: where each one sits, what fits in the note, and what survives the note being cut, shared by the vibrato and the tremolo.
public static class NoteSweep
{
    public const int MinDelayTicks = 0;
    public const int MinLengthTicks = 1;

    public static readonly ValueRange DelayTicksRange = new(MinDelayTicks, SnesSweep.MaxDelay);
    public static readonly ValueRange LengthTicksRange = new(MinLengthTicks, int.MaxValue);

    // Ticks from where a sweep's delay starts counting to where the next one begins counting.
    public static int SpanTicks<T>(T sweep) where T : struct, INoteSweep<T>
    {
        return sweep.DelayTicks + sweep.LengthTicks;
    }

    // A sweep with its delay held between none and the longest its command's delay byte takes, and its length to at least the shortest - an endless one's to
    // exactly the shortest, its notes giving it the rest.
    public static T WithSpanClamped<T>(T sweep, int maxDelayTicks) where T : struct, INoteSweep<T>
    {
        int lengthTicks = sweep.IsEndless ? MinLengthTicks : Math.Max(sweep.LengthTicks, MinLengthTicks);
        return sweep.WithSpan(Math.Clamp(sweep.DelayTicks, MinDelayTicks, maxDelayTicks), lengthTicks);
    }

    // A sweep lasting a number of ticks, no longer running on to the note's end, with its delay and its own values untouched.
    public static T WithFixedLength<T>(T sweep, int lengthTicks) where T : struct, INoteSweep<T>
    {
        return sweep.WithSpan(sweep.DelayTicks, lengthTicks).WithEndless(false);
    }

    // Ticks from a note's own start to where the sweep at an index begins counting its delay: the spans of everything in front of it.
    public static int SpanBefore<T>(ImmutableArray<T> sweeps, int sweepIndex) where T : struct, INoteSweep<T>
    {
        if (sweeps.IsDefault)
        {
            return 0;
        }

        int spanTicks = 0;
        for (int index = 0; index < sweepIndex && index < sweeps.Length; index++)
        {
            spanTicks += SpanTicks(sweeps[index]);
        }

        return spanTicks;
    }

    // The longest delay the sweep at an index can hold on a note of a length while still leaving itself its shortest length, negative where there is no room at all.
    public static int MaxDelayAt<T>(ImmutableArray<T> sweeps, int sweepIndex, int noteLengthTicks) where T : struct, INoteSweep<T>
    {
        return noteLengthTicks - MinLengthTicks - SpanBefore(sweeps, sweepIndex);
    }

    // The longest length the sweep at an index can hold: whatever is left of the note once everything in front of it and its own delay are taken off.
    public static int MaxLengthAt<T>(ImmutableArray<T> sweeps, int sweepIndex, int noteLengthTicks) where T : struct, INoteSweep<T>
    {
        if (sweepIndex >= sweeps.Length)
        {
            return 0;
        }

        return noteLengthTicks - SpanBefore(sweeps, sweepIndex) - sweeps[sweepIndex].DelayTicks;
    }

    // The sweeps a note of a length can play: one with no room left to start is taken off with everything behind it, one straddling the end is cut, and an endless one runs to the end.
    public static ImmutableArray<T> FitToNoteLength<T>(ImmutableArray<T> sweeps, int noteLengthTicks) where T : struct, INoteSweep<T>
    {
        if (sweeps.IsDefaultOrEmpty)
        {
            return [];
        }

        var fittedSweeps = ImmutableArray.CreateBuilder<T>(sweeps.Length);
        int previousEndTick = 0;
        foreach (var sweep in sweeps)
        {
            int startTick = previousEndTick + sweep.DelayTicks;
            if (startTick + MinLengthTicks > noteLengthTicks)
            {
                break;
            }

            int fittedLengthTicks = sweep.IsEndless ? noteLengthTicks - startTick : Math.Min(sweep.LengthTicks, noteLengthTicks - startTick);
            fittedSweeps.Add(sweep.WithSpan(sweep.DelayTicks, fittedLengthTicks));
            previousEndTick = startTick + fittedLengthTicks;
        }

        return fittedSweeps.ToImmutable();
    }

    // The chain with its last sweep running on to the note's end, where it already ends exactly there.
    public static ImmutableArray<T> WithEndlessTail<T>(ImmutableArray<T> sweeps, int noteLengthTicks) where T : struct, INoteSweep<T>
    {
        if (sweeps.IsDefaultOrEmpty || SpanBefore(sweeps, sweeps.Length) != noteLengthTicks)
        {
            return sweeps;
        }

        return sweeps.SetItem(sweeps.Length - 1, sweeps[^1].WithEndless(true));
    }

    // Whether two chains play the same on a note of a length, a last sweep ending exactly at the note's end being the same whether it runs on to the end or not.
    public static bool PlaySameOnNote<T>(ImmutableArray<T> first, ImmutableArray<T> second, int noteLengthTicks) where T : struct, INoteSweep<T>
    {
        return WithEndlessTail(first, noteLengthTicks).SequenceEqual(WithEndlessTail(second, noteLengthTicks));
    }

    // The chain rebased onto a note cut to start some ticks in and to a new length: each sweep keeps the ticks of itself that survive, and one entirely outside is dropped.
    public static ImmutableArray<T> CutToSpan<T>(ImmutableArray<T> sweeps, int trimmedTicks, int lengthTicks) where T : struct, INoteSweep<T>
    {
        if (sweeps.IsDefaultOrEmpty)
        {
            return [];
        }

        var cutSweeps = ImmutableArray.CreateBuilder<T>(sweeps.Length);
        int previousEndTick = 0;
        int previousKeptEndTick = 0;
        foreach (var sweep in sweeps)
        {
            int startTick = previousEndTick + sweep.DelayTicks;
            int endTick = startTick + sweep.LengthTicks;
            previousEndTick = endTick;
            int keptStartTick = Math.Max(startTick, trimmedTicks) - trimmedTicks;
            int keptEndTick = Math.Min(endTick, trimmedTicks + lengthTicks) - trimmedTicks;
            if (keptEndTick - keptStartTick < MinLengthTicks)
            {
                continue;
            }

            cutSweeps.Add(sweep.WithSpan(keptStartTick - previousKeptEndTick, keptEndTick - keptStartTick));
            previousKeptEndTick = keptEndTick;
        }

        return cutSweeps.ToImmutable();
    }
}
