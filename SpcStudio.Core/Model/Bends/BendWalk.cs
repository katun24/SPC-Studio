using System.Collections.Immutable;
using SpcStudio.Export;

namespace SpcStudio.Model;

// The driver's $DD read-ahead and ramp, walked over one note a tick at a time: which tick each command is read on, and which ticks its ramp steps on.
public static class BendWalk
{
    public const int KeyOnPiece = -1;

    // The ramps a chain plays as, in order, with the index of the first command a fetch reached unread where one did, which stops the driver.
    public static (ImmutableArray<PlayedRamp> Ramps, int? StoppedAtIndex) WalkChain(Layout layout, IReadOnlyList<WrittenRamp> chain)
    {
        var ramps = new List<PlayedRamp>(chain.Count);
        int nextRampIndex = 0;
        int pieceStart = 0;
        int delayLeft = 0;
        int stepsLeft = 0;
        int readTick = 0;
        int firstStepTick = -1;
        if (chain.Count > 0 && chain[0].PieceStart == KeyOnPiece)
        {
            delayLeft = chain[0].DelayByte;
            stepsLeft = chain[0].LengthByte;
            nextRampIndex = 1;
        }

        // The last tick walked, far enough past the note and its pieces for the last ramp's delay and length bytes to run out.
        int lastWalkedTick = Math.Max(layout.NoteTicks, layout.WrittenTicks) + 2 * (PitchBend.MaxDelayByte + 1);
        for (int tick = 1; tick <= lastWalkedTick; tick++)
        {
            bool isRampUnread = nextRampIndex < chain.Count;
            if (tick < layout.WrittenTicks && layout.PieceStarts.BinarySearch(tick) >= 0)
            {
                if (isRampUnread && chain[nextRampIndex].PieceStart == pieceStart)
                {
                    return ([.. ramps], nextRampIndex);
                }

                pieceStart = tick;
                continue;
            }

            if (tick == layout.WrittenTicks && isRampUnread)
            {
                return ([.. ramps], nextRampIndex);
            }

            if (layout.IsFetchTick(tick))
            {
                continue;
            }

            if (stepsLeft == 0 && isRampUnread && tick < layout.WrittenTicks && chain[nextRampIndex].PieceStart == pieceStart)
            {
                delayLeft = chain[nextRampIndex].DelayByte;
                stepsLeft = chain[nextRampIndex].LengthByte;
                readTick = tick;
                firstStepTick = -1;
                nextRampIndex++;
            }

            if (delayLeft > 0)
            {
                delayLeft--;
            }
            else if (stepsLeft > 0)
            {
                if (firstStepTick < 0)
                {
                    firstStepTick = tick;
                }

                stepsLeft--;
                if (stepsLeft == 0)
                {
                    ramps.Add(new PlayedRamp(readTick, firstStepTick, tick));
                }
            }

            if (nextRampIndex == chain.Count && stepsLeft == 0 && tick >= layout.WrittenTicks)
            {
                break;
            }
        }

        return ([.. ramps], null);
    }

    // Where a note's pieces start and end: the first starts on the note's own tick, which is zero here, and every later one is a tick that fetches.
    public sealed record Layout(ImmutableArray<int> PieceStarts, int WrittenTicks, int NoteTicks)
    {
        // A note's pieces as AddmusicK compiles what the exporter writes: broken at every break tick and at every piece a bend is written behind, and
        // each stretch between two of those split the way AddmusicK splits a run.
        public static Layout CompiledFrom(IEnumerable<int> breakTicks, IEnumerable<int> bendPieceStarts, int writtenTicks, int noteTicks)
        {
            var bendPieceStartSet = bendPieceStarts.ToHashSet();

            // Every tick a run is broken at, from zero to the written end.
            var runBoundaries = breakTicks.Concat(bendPieceStartSet)
                .Where(tick => tick > 0 && tick < writtenTicks)
                .Append(0)
                .Distinct()
                .Order()
                .Append(writtenTicks)
                .ToList();
            var pieceStarts = ImmutableArray.CreateBuilder<int>();
            for (int boundaryIndex = 0; boundaryIndex + 1 < runBoundaries.Count; boundaryIndex++)
            {
                int pieceStart = runBoundaries[boundaryIndex];
                int runEnd = runBoundaries[boundaryIndex + 1];
                if (bendPieceStartSet.Contains(pieceStart))
                {
                    pieceStart = AddPieceStarts(pieceStarts, pieceStart, [Math.Min(runEnd - pieceStart, MmlDurationEncoding.MaxTicksWithoutAutoTies)]);
                }

                if (pieceStart >= runEnd)
                {
                    continue;
                }

                AddPieceStarts(pieceStarts, pieceStart, MmlDurationEncoding.SplitRunIntoTiedPieces(runEnd - pieceStart));
            }

            return new Layout(pieceStarts.ToImmutable(), writtenTicks, noteTicks);
        }

        // Pieces laid end to end from the note's start, written up to where the last of them ends.
        public static Layout OfPieces(IEnumerable<int> pieceTicks, int noteTicks)
        {
            var pieceStarts = ImmutableArray.CreateBuilder<int>();
            int writtenTicks = AddPieceStarts(pieceStarts, 0, pieceTicks);
            return new Layout(pieceStarts.ToImmutable(), writtenTicks, noteTicks);
        }

        // Whether a tick is one the ramp cannot step on: a tie's own, or the fetch of whatever follows pieces that end before the note does.
        public bool IsFetchTick(int tick)
        {
            if (tick > 0 && tick < WrittenTicks)
            {
                return PieceStarts.BinarySearch(tick) >= 0;
            }

            return tick == WrittenTicks && WrittenTicks < NoteTicks;
        }

        // The first tick at or after a tick that the ramp can step on.
        public int FirstSteppableTickFrom(int tick)
        {
            while (IsFetchTick(tick))
            {
                tick++;
            }

            return tick;
        }

        // How many ticks between two ticks, both included, the ramp can step on.
        public int SteppableTicksBetween(int firstTick, int lastTick)
        {
            int steppableTickCount = 0;
            for (int tick = Math.Max(firstTick, 1); tick <= lastTick; tick++)
            {
                if (!IsFetchTick(tick))
                {
                    steppableTickCount++;
                }
            }

            return steppableTickCount;
        }

        // A ramp the song says steps first on one tick and ends on another, as the length byte that makes it end there and the tick of its last step.
        public (int LengthByte, int LastStepTick) RampEndingAt(int firstStepTick, int endTick)
        {
            // The steppable ticks from the first step to the end, never fewer than the shortest ramp takes.
            int lengthByte = Math.Max(SteppableTicksBetween(firstStepTick, endTick), PitchBend.MinLengthTicks);
            return (lengthByte, LastStepTick(firstStepTick, lengthByte));
        }

        // The tick a ramp stepping first on a tick takes its last step on, given its length byte.
        public int LastStepTick(int firstStepTick, int lengthByte)
        {
            int lastStepTick = firstStepTick;
            for (int stepsLeft = lengthByte - 1; stepsLeft > 0; stepsLeft--)
            {
                lastStepTick = FirstSteppableTickFrom(lastStepTick + 1);
            }

            return lastStepTick;
        }

        // Adds the first tick of each of a run of pieces laid end to end from a tick, and answers the tick the last of them ends on.
        private static int AddPieceStarts(ImmutableArray<int>.Builder pieceStarts, int fromTick, IEnumerable<int> pieceTicks)
        {
            int pieceStart = fromTick;
            foreach (int ticks in pieceTicks)
            {
                pieceStarts.Add(pieceStart);
                pieceStart += ticks;
            }

            return pieceStart;
        }
    }

    // One ramp as the stream holds it: the start of the piece its $DD is written behind, and its delay and length bytes.
    public readonly record struct WrittenRamp(int PieceStart, int DelayByte, int LengthByte);

    // What the driver made of one ramp: the tick its command was read on, and the ticks of its first and last step.
    public readonly record struct PlayedRamp(int ReadTick, int FirstStepTick, int LastStepTick);
}
