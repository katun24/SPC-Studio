using System.Collections.Immutable;

namespace SpcStudio.Model;

// One $DD on a note: the ticks before its ramp starts and the ticks it runs for, and the semitones it moves, each counted from where the bend in front of it ended.
public readonly record struct PitchBend(int DelayTicks, int LengthTicks, int OffsetSemitones) : INoteSweep<PitchBend>
{
    public const int MinDelayTicks = 0;
    public const int MaxDelayByte = 255;
    public const int MinLengthTicks = 1;
    public const int MaxLengthTicks = 255;
    public const int MaxReachSemitones = Song.HighestSemitone - Song.LowestSemitone;

    public static readonly ValueRange DelayRange = new(MinDelayTicks, MaxDelayByte);
    public static readonly ValueRange LengthTicksRange = new(MinLengthTicks, MaxLengthTicks);

    public bool IsEndless { get; init; }

    // This bend with its delay and length replaced and its offset untouched.
    public PitchBend WithSpan(int delayTicks, int lengthTicks)
    {
        return this with { DelayTicks = delayTicks, LengthTicks = lengthTicks };
    }

    // This bend's ramp running on to the end of every note playing it, or lasting its own length, with its other values untouched.
    public PitchBend WithEndless(bool isEndless)
    {
        return this with { IsEndless = isEndless };
    }

    // The longest delay the bend at an index can hold on a note of a length, its ramp having to start on a tick the note still sounds on - negative where
    // the chain in front of it already outlives the note.
    public static int MaxDelayAt(ImmutableArray<PitchBend> bends, int bendIndex, int noteLengthTicks)
    {
        int noteLastTick = noteLengthTicks - 1;
        return noteLastTick - NoteSweep.SpanBefore(bends, bendIndex);
    }

    // Whether the ramp of the bend at an index can run on to the end of a note of a length, which the length byte can say only of a short enough ramp.
    public static bool CanRunToNoteEnd(ImmutableArray<PitchBend> bends, int bendIndex, int noteLengthTicks)
    {
        int startTick = NoteSweep.SpanBefore(bends, bendIndex) + bends[bendIndex].DelayTicks;
        return noteLengthTicks - startTick <= MaxLengthTicks;
    }

    // The ramp the driver sets a bend out on when it reads it at a 16-bit note value, towards a target note value over the bend's length.
    public static SnesFade RampFrom(int noteValue16, int targetNoteValue, int lengthTicks)
    {
        return SnesFade.SetOutFrom(noteValue16).FadingTo(targetNoteValue, lengthTicks);
    }

    // Semitones from a note's own pitch to where the bend at an index starts ramping: the offsets of everything in front of it.
    public static int OffsetBefore(ImmutableArray<PitchBend> bends, int bendIndex)
    {
        int offsetSemitones = 0;
        for (int index = 0; index < bendIndex && index < bends.Length; index++)
        {
            offsetSemitones += bends[index].OffsetSemitones;
        }

        return offsetSemitones;
    }

    // The bends a note of a length can play: every one whose ramp would start at or after the note's end is taken off, which is always a tail of the chain, and an endless one's ramp runs to the end.
    public static ImmutableArray<PitchBend> FitToNoteLength(ImmutableArray<PitchBend> bends, int noteLengthTicks)
    {
        if (bends.IsDefaultOrEmpty)
        {
            return [];
        }

        var fittedBends = ImmutableArray.CreateBuilder<PitchBend>(bends.Length);
        int previousEndTick = 0;
        foreach (var bend in bends)
        {
            int startTick = previousEndTick + bend.DelayTicks;
            if (startTick >= noteLengthTicks)
            {
                break;
            }

            var fittedBend = bend.IsEndless ? RampOverRest(bend, noteLengthTicks - startTick) : bend;
            fittedBends.Add(fittedBend);
            previousEndTick = startTick + fittedBend.LengthTicks;
        }

        return fittedBends.ToImmutable();
    }

    // The lowest and the highest pitch a chain reaches, in semitones from wherever it starts, both zero for an empty chain.
    public static (int Lowest, int Highest) Reach(IEnumerable<PitchBend> bends)
    {
        int pitchSemitones = 0;
        int lowestSemitones = 0;
        int highestSemitones = 0;
        foreach (var bend in bends)
        {
            pitchSemitones += bend.OffsetSemitones;
            lowestSemitones = Math.Min(lowestSemitones, pitchSemitones);
            highestSemitones = Math.Max(highestSemitones, pitchSemitones);
        }

        return (lowestSemitones, highestSemitones);
    }

    // The pitches a chain of bends can start from while every pitch it carries the note to stays inside a band.
    public static ValueRange StartsHeldIn(ValueRange band, IEnumerable<PitchBend> bends)
    {
        var (lowestSemitones, highestSemitones) = Reach(bends);
        return band.HoldingReach(lowestSemitones, highestSemitones);
    }

    // Where in a note each bend of the chain is written and the delay and length bytes it carries there, for as many bends at the head of the chain as the note can hold.
    public static ImmutableArray<BendPlacement> Placements(ImmutableArray<PitchBend> bends, int noteLengthTicks, int? writtenTicks = null, IEnumerable<int>? breakTicks = null)
    {
        if (bends.IsDefaultOrEmpty)
        {
            return [];
        }

        int writtenEndTick = writtenTicks ?? noteLengthTicks;
        var breakTickList = (breakTicks ?? []).ToList();
        var placedBends = new List<PlacedBend>(bends.Length);
        int previousEndTick = 0;
        foreach (var bend in bends)
        {
            int armTick = Math.Clamp(previousEndTick + bend.DelayTicks, 0, noteLengthTicks - 1);
            int endTick = armTick + bend.LengthTicks;
            bool isFullLength = bend.LengthTicks == MaxLengthTicks;

            // The bend placed behind the ones already placed, or null where it cannot be.
            var placement = Place(armTick, endTick, isFullLength, placedBends, breakTickList, writtenEndTick, noteLengthTicks);
            if (placement is null && placedBends.Count > 0 && placedBends[^1].IsFullLength)
            {
                placedBends[^1] = placedBends[^1] with { IsFullLength = false };
                placement = Place(armTick, endTick, isFullLength, placedBends, breakTickList, writtenEndTick, noteLengthTicks);
                if (placement is null)
                {
                    placedBends[^1] = placedBends[^1] with { IsFullLength = true };
                }
            }

            if (placement is null)
            {
                break;
            }

            placedBends.Add(placement.Value);
            previousEndTick = endTick;
        }

        var layout = LayoutWith(breakTickList, placedBends, null, writtenEndTick, noteLengthTicks);
        return [.. placedBends.Select(placed => new BendPlacement(placed.PieceStart, placed.DelayByte, StepsOf(layout, placed).LengthByte))];
    }

    // The part of an instrument's chain a note at a pitch and a length plays: everything in front of the first bend that does not fit the note, would carry it
    // out of the roll, or could not be placed where the driver reads it on its tick.
    public static ImmutableArray<PitchBend> Carried(ImmutableArray<PitchBend> bends, int semitone, int noteLengthTicks, bool isHeld = false)
    {
        return WithinRoll(Playable(bends, noteLengthTicks, isHeld), semitone);
    }

    // The part of a chain a note at a pitch plays without leaving the roll: everything in front of the first bend that would carry it out.
    public static ImmutableArray<PitchBend> WithinRoll(ImmutableArray<PitchBend> bends, int semitone)
    {
        int keptCount = bends.Length;
        int pitchSemitone = semitone;
        for (int index = 0; index < keptCount; index++)
        {
            pitchSemitone += bends[index].OffsetSemitones;
            if (pitchSemitone < Song.LowestSemitone || pitchSemitone > Song.HighestSemitone)
            {
                keptCount = index;
                break;
            }
        }

        return FirstBends(bends, keptCount);
    }

    // The part of an instrument's chain a note of a length plays wherever it sits, which is what the note's row has to leave room for.
    public static ImmutableArray<PitchBend> Playable(ImmutableArray<PitchBend> bends, int noteLengthTicks, bool isHeld = false)
    {
        var fittedBends = FitToNoteLength(bends, noteLengthTicks);

        // Where a held note's pieces end, a tick short of the note.
        int? writtenTicks = isHeld && noteLengthTicks > 1 ? noteLengthTicks - 1 : null;

        int placedCount = Placements(fittedBends, noteLengthTicks, writtenTicks).Length;
        return FirstBends(fittedBends, placedCount);
    }

    // A chain with every bend that moves no pitch taken off, the ticks it held going to the delay of the bend behind it.
    public static ImmutableArray<PitchBend> WithoutLevelBends(ImmutableArray<PitchBend> bends)
    {
        if (bends.All(bend => bend.OffsetSemitones != 0))
        {
            return bends;
        }

        var movingBends = ImmutableArray.CreateBuilder<PitchBend>(bends.Length);
        int carriedTicks = 0;
        foreach (var bend in bends)
        {
            if (bend.OffsetSemitones == 0)
            {
                carriedTicks += NoteSweep.SpanTicks(bend);
                continue;
            }

            movingBends.Add(bend with { DelayTicks = bend.DelayTicks + carriedTicks });
            carriedTicks = 0;
        }

        return movingBends.ToImmutable();
    }

    // A chain with every value held to what the model and the driver take, for an instrument's, cut before the first bend that would make it wider than the
    // roll, with no bend that moves no pitch, and an endless ramp exactly the shortest, its notes giving it the rest.
    public static ImmutableArray<PitchBend> Sanitized(ImmutableArray<PitchBend> bends)
    {
        if (bends.IsDefaultOrEmpty)
        {
            return [];
        }

        bends = WithoutLevelBends(bends);
        var sanitizedBends = ImmutableArray.CreateBuilder<PitchBend>(bends.Length);
        for (int index = 0; index < bends.Length; index++)
        {
            var (lowestSemitones, highestSemitones) = Reach(bends.Take(index + 1));
            if (highestSemitones - lowestSemitones > MaxReachSemitones)
            {
                break;
            }

            var bend = bends[index];
            int lengthTicks = bend.IsEndless ? MinLengthTicks : LengthTicksRange.Clamp(bend.LengthTicks);
            sanitizedBends.Add(bend.WithSpan(Math.Max(bend.DelayTicks, MinDelayTicks), lengthTicks));
        }

        return sanitizedBends.ToImmutable();
    }

    // An endless bend with its ramp over the ticks left of its note, or no longer endless and as long as a ramp can be where those are more than that.
    private static PitchBend RampOverRest(PitchBend bend, int restTicks)
    {
        if (restTicks > MaxLengthTicks)
        {
            return bend with { LengthTicks = MaxLengthTicks, IsEndless = false };
        }

        return bend with { LengthTicks = restTicks };
    }

    // The first bends of a chain, the whole chain where the count reaches its end.
    private static ImmutableArray<PitchBend> FirstBends(ImmutableArray<PitchBend> bends, int keptCount)
    {
        if (keptCount >= bends.Length)
        {
            return bends;
        }

        if (keptCount == 0)
        {
            return [];
        }

        return ImmutableArray.Create(bends, 0, keptCount);
    }

    // The piece and the delay byte for one bend behind those already placed, or null where the note has none that plays it on its tick.
    private static PlacedBend? Place(int armTick, int endTick, bool isFullLength, List<PlacedBend> placedBends, List<int> breakTicks, int writtenEndTick, int noteLengthTicks)
    {
        var layout = LayoutWith(breakTicks, placedBends, null, writtenEndTick, noteLengthTicks);
        int lowestPieceStart = placedBends.Count == 0 ? 0 : placedBends[^1].PieceStart;
        int firstStepTick = FirstStepTick(layout, armTick, placedBends);
        int pieceBeforeFirstStep = firstStepTick - 1;

        // The piece starting on the tick in front of the first step, then every earlier one back to the piece the bend in front sits behind.
        var candidatePieceStarts = new List<int>();
        if (pieceBeforeFirstStep >= lowestPieceStart && pieceBeforeFirstStep < writtenEndTick)
        {
            candidatePieceStarts.Add(pieceBeforeFirstStep);
        }

        for (int pieceIndex = layout.PieceStarts.Length - 1; pieceIndex >= 0; pieceIndex--)
        {
            int pieceStart = layout.PieceStarts[pieceIndex];
            if (pieceStart < pieceBeforeFirstStep && pieceStart >= lowestPieceStart)
            {
                candidatePieceStarts.Add(pieceStart);
            }
        }

        PlacedBend? bestPlacement = null;
        int bestLastStepInFront = -1;
        foreach (int pieceStart in candidatePieceStarts)
        {
            var trialLayout = LayoutWith(breakTicks, placedBends, pieceStart, writtenEndTick, noteLengthTicks);
            int stepTick = FirstStepTick(trialLayout, armTick, placedBends);
            if (pieceStart >= stepTick)
            {
                continue;
            }

            var undelayedBend = new PlacedBend(stepTick, endTick, pieceStart, 0, isFullLength);
            var (undelayedRamps, undelayedStoppedAt) = BendWalk.WalkChain(trialLayout, WrittenChain(trialLayout, placedBends, undelayedBend));
            if (!IsNewBendPlayed(undelayedRamps, undelayedStoppedAt, placedBends))
            {
                continue;
            }

            int readTick = undelayedRamps[placedBends.Count].ReadTick;
            if (readTick > stepTick)
            {
                continue;
            }

            int delayByte = trialLayout.SteppableTicksBetween(readTick + 1, stepTick);
            if (delayByte > MaxDelayByte)
            {
                continue;
            }

            var candidate = new PlacedBend(stepTick, endTick, pieceStart, delayByte, isFullLength);
            var (ramps, stoppedAt) = BendWalk.WalkChain(trialLayout, WrittenChain(trialLayout, placedBends, candidate));
            if (!IsNewBendPlayed(ramps, stoppedAt, placedBends) || ramps[placedBends.Count].FirstStepTick != stepTick)
            {
                continue;
            }

            int lastStepInFront = placedBends.Count == 0 ? 0 : ramps[placedBends.Count - 1].LastStepTick;
            if (lastStepInFront > bestLastStepInFront)
            {
                bestPlacement = candidate;
                bestLastStepInFront = lastStepInFront;
            }
        }

        return bestPlacement;
    }

    // The length byte a placed bend is written with and the tick of its last step: the full byte for a bend of the longest length, and otherwise the count that ends it where the song says.
    private static (int LengthByte, int LastStepTick) StepsOf(BendWalk.Layout layout, PlacedBend placed)
    {
        if (placed.IsFullLength)
        {
            return (MaxLengthTicks, layout.LastStepTick(placed.FirstStepTick, MaxLengthTicks));
        }

        return layout.RampEndingAt(placed.FirstStepTick, placed.EndTick);
    }

    // The note's pieces with the bends placed so far written into it, and one more where a piece start is given.
    private static BendWalk.Layout LayoutWith(List<int> breakTicks, List<PlacedBend> placedBends, int? extraPieceStart, int writtenEndTick, int noteLengthTicks)
    {
        var bendPieceStarts = placedBends.Select(placed => placed.PieceStart);
        if (extraPieceStart is { } pieceStart)
        {
            bendPieceStarts = bendPieceStarts.Append(pieceStart);
        }

        return BendWalk.Layout.CompiledFrom(breakTicks, bendPieceStarts, writtenEndTick, noteLengthTicks);
    }

    // The first tick a bend arming at a tick can step on: the tick after its arm, or after the last step of the ramp in front where that runs later, moved on past whatever fetches there.
    private static int FirstStepTick(BendWalk.Layout layout, int armTick, List<PlacedBend> placedBends)
    {
        int earliestTick = armTick + 1;
        if (placedBends.Count > 0)
        {
            var bendInFront = placedBends[^1];
            earliestTick = Math.Max(earliestTick, layout.RampEndingAt(bendInFront.FirstStepTick, bendInFront.EndTick).LastStepTick + 1);
        }

        return layout.FirstSteppableTickFrom(earliestTick);
    }

    // The placed bends and one more, as the stream would hold them on a layout.
    private static List<BendWalk.WrittenRamp> WrittenChain(BendWalk.Layout layout, List<PlacedBend> placedBends, PlacedBend newBend)
    {
        return [.. placedBends.Append(newBend).Select(placed => new BendWalk.WrittenRamp(placed.PieceStart, placed.DelayByte, StepsOf(layout, placed).LengthByte))];
    }

    // Whether a walk read the bend behind the placed ones without stopping, every placed bend still first stepping on the tick it was placed to.
    private static bool IsNewBendPlayed(ImmutableArray<BendWalk.PlayedRamp> ramps, int? stoppedAtIndex, List<PlacedBend> placedBends)
    {
        if (stoppedAtIndex is not null || ramps.Length <= placedBends.Count)
        {
            return false;
        }

        for (int index = 0; index < placedBends.Count; index++)
        {
            if (ramps[index].FirstStepTick != placedBends[index].FirstStepTick)
            {
                return false;
            }
        }

        return true;
    }

    // One bend placed: the tick its ramp should first step on and end on, the piece and the delay byte that make it do so, and whether it is written with the full length byte.
    private readonly record struct PlacedBend(int FirstStepTick, int EndTick, int PieceStart, int DelayByte, bool IsFullLength);
}
