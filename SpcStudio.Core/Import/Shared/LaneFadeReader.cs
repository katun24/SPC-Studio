using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Import;

// An imported lane's values as they were played, read back as the fewest sets and fades that play within a tolerance of them.
public static class LaneFadeReader
{
    // The changes a lane's points are written as, the first standing for the base: a fade over every run of moves one fade from the value in force plays
    // within the tolerance, where that takes fewer changes than a set at each, and a set at every other move.
    public static List<WrittenLaneChange<TKey>> Read<TKey>(IEnumerable<LanePoint<TKey>> points, int tolerance, int loopTick, int rollTicks)
    {
        var moves = Moves(points);
        var changes = new List<WrittenLaneChange<TKey>>();
        if (moves.Count == 0)
        {
            return changes;
        }

        changes.Add(SetAt(moves[0]));

        int moveIndex = 1;

        // The tick of the last change where it only ends the fade before it, which the next fade may start on in its place.
        int? endDividerTick = null;
        while (moveIndex < moves.Count)
        {
            var firstMove = moves[moveIndex];
            var previousMove = moves[moveIndex - 1];
            bool hasKeyMoved = !EqualityComparer<TKey>.Default.Equals(firstMove.Key, previousMove.Key);
            int earliestFadeStartTick = hasKeyMoved ? firstMove.Tick : endDividerTick ?? changes[^1].Tick + 1;
            var (lastMoveIndex, startTick) = LongestFade(moves, moveIndex, earliestFadeStartTick, tolerance, loopTick);
            var lastMove = moves[lastMoveIndex];
            int fadeEndTick = lastMove.Tick + 1;
            bool doesFadeNeedEndDivider = fadeEndTick < rollTicks && (lastMoveIndex + 1 == moves.Count || moves[lastMoveIndex + 1].Tick != fadeEndTick);
            int fadeChangeCount = doesFadeNeedEndDivider ? 2 : 1;
            if (fadeChangeCount >= lastMoveIndex - moveIndex + 1)
            {
                changes.Add(SetAt(firstMove));
                endDividerTick = null;
                moveIndex++;
                continue;
            }

            if (startTick == endDividerTick)
            {
                changes.RemoveAt(changes.Count - 1);
            }

            changes.Add(new WrittenLaneChange<TKey>(startTick, previousMove.Values, firstMove.Key, true, lastMove.Values));
            endDividerTick = doesFadeNeedEndDivider ? fadeEndTick : null;
            if (doesFadeNeedEndDivider)
            {
                changes.Add(SetAt(lastMove with { Tick = fadeEndTick }));
            }

            moveIndex = lastMoveIndex + 1;
        }

        return changes;
    }

    // A lane's base written from the first of what was read, and each after it as a change.
    public static void WriteBaseThenChanges<TChange>(IReadOnlyList<TChange> changes, Action<TChange> setBase, Action<TChange> setChange)
    {
        for (int changeIndex = 0; changeIndex < changes.Count; changeIndex++)
        {
            if (changeIndex == 0)
            {
                setBase(changes[changeIndex]);
                continue;
            }

            setChange(changes[changeIndex]);
        }
    }

    // The points with only the last at each tick kept and every one that moves nothing dropped, a tick before zero counted as zero.
    public static List<LanePoint<TKey>> Moves<TKey>(IEnumerable<LanePoint<TKey>> points)
    {
        var moves = new List<LanePoint<TKey>>();
        foreach (var point in points)
        {
            // The point, a tick before zero counted as zero.
            var move = point with { Tick = Math.Max(point.Tick, 0) };
            if (moves.Count > 0 && moves[^1].Tick == move.Tick)
            {
                moves.RemoveAt(moves.Count - 1);
            }

            if (moves.Count > 0 && IsSameValue(moves[^1], move))
            {
                continue;
            }

            moves.Add(move);
        }

        return moves;
    }

    // A change that sets a point's values and fades nowhere.
    private static WrittenLaneChange<TKey> SetAt<TKey>(LanePoint<TKey> point)
    {
        return new WrittenLaneChange<TKey>(point.Tick, point.Values, point.Key, false, point.Values);
    }

    // Whether two points hold the same values under the same key.
    private static bool IsSameValue<TKey>(LanePoint<TKey> first, LanePoint<TKey> second)
    {
        return first.Values.SequenceEqual(second.Values) && EqualityComparer<TKey>.Default.Equals(first.Key, second.Key);
    }

    // The furthest move one fade from the value in force can land on, playing every tick from its start within the tolerance, under one key and not across the loop tick, with a start it plays from -
    // kept from the move before while it still fits - or the first move and its own tick where no fade reaches past it.
    private static (int LastMoveIndex, int StartTick) LongestFade<TKey>(List<LanePoint<TKey>> moves, int firstMoveIndex, int earliestStartTick, int tolerance, int loopTick)
    {
        var firstMove = moves[firstMoveIndex];
        bool isLoopBeforeFirstMove = loopTick > earliestStartTick && loopTick <= firstMove.Tick;
        int earliestStartAfterLoop = isLoopBeforeFirstMove ? loopTick : earliestStartTick;
        var longestFadeSoFar = (LastMoveIndex: firstMoveIndex, StartTick: firstMove.Tick);
        for (int candidateIndex = firstMoveIndex + 1; candidateIndex < moves.Count; candidateIndex++)
        {
            var candidate = moves[candidateIndex];
            bool crossesLoop = loopTick > firstMove.Tick && loopTick <= candidate.Tick;
            if (!EqualityComparer<TKey>.Default.Equals(candidate.Key, firstMove.Key) || crossesLoop)
            {
                break;
            }

            if (FadeStray(moves, longestFadeSoFar.StartTick, firstMoveIndex, candidateIndex, tolerance) <= tolerance)
            {
                longestFadeSoFar = (candidateIndex, longestFadeSoFar.StartTick);
                continue;
            }

            if (ClosestStartTick(moves, firstMoveIndex, candidateIndex, earliestStartAfterLoop, tolerance) is not int startTick)
            {
                break;
            }

            longestFadeSoFar = (candidateIndex, startTick);
        }

        return longestFadeSoFar;
    }

    // The tick one fade landing on a move starts from to play every tick up to it within the tolerance, the closest playing and of those the latest,
    // searched back from the first move only as far as the fade's own pace could have hidden its first step, or null where no start does.
    private static int? ClosestStartTick<TKey>(List<LanePoint<TKey>> moves, int firstMoveIndex, int lastMoveIndex, int earliestStartTick, int tolerance)
    {
        var inForce = moves[firstMoveIndex - 1].Values;
        var lastMove = moves[lastMoveIndex];
        int firstMoveTick = moves[firstMoveIndex].Tick;
        int furthestSideDistance = Enumerable.Range(0, inForce.Length).Max(side => Math.Abs(lastMove.Values[side] - inForce[side]));
        int fadeTicksPerUnit = furthestSideDistance == 0 ? 0 : (lastMove.Tick - firstMoveTick + furthestSideDistance) / furthestSideDistance;
        int searchFromTick = Math.Max(earliestStartTick, firstMoveTick - fadeTicksPerUnit);
        int? closestStartTick = null;
        int closestStray = tolerance + 1;
        for (int startTick = firstMoveTick; startTick >= searchFromTick; startTick--)
        {
            int stray = FadeStray(moves, startTick, firstMoveIndex, lastMoveIndex, tolerance);
            if (stray < closestStray)
            {
                closestStray = stray;
                closestStartTick = startTick;
            }
        }

        return closestStartTick;
    }

    // The furthest one fade from the value in force, starting on a tick and landing on a move's values on its own tick, strays on any tick and side from
    // the moves in force there, counted no further than past the tolerance, each move's hold measured at its first and last tick.
    private static int FadeStray<TKey>(List<LanePoint<TKey>> moves, int startTick, int firstMoveIndex, int lastMoveIndex, int tolerance)
    {
        var inForce = moves[firstMoveIndex - 1].Values;
        int lastTick = moves[lastMoveIndex].Tick;
        int lengthTicks = lastTick - startTick + 1;
        int furthestStray = 0;
        for (int side = 0; side < inForce.Length; side++)
        {
            var playedSideFade = ChangeMap.ForRegions(inForce[side], [new LaneRegion(0, lengthTicks, inForce[side], true, moves[lastMoveIndex].Values[side])]);
            for (int moveIndex = firstMoveIndex - 1; moveIndex <= lastMoveIndex; moveIndex++)
            {
                int holdStartTick = Math.Max(moves[moveIndex].Tick, startTick);
                int holdEndTick = moveIndex == lastMoveIndex ? lastTick : moves[moveIndex + 1].Tick - 1;
                if (holdEndTick < holdStartTick)
                {
                    continue;
                }

                int holdStray = Math.Max(
                    Math.Abs(playedSideFade.InForceAt(holdStartTick - startTick) - moves[moveIndex].Values[side]),
                    Math.Abs(playedSideFade.InForceAt(holdEndTick - startTick) - moves[moveIndex].Values[side]));
                if (holdStray > tolerance)
                {
                    return holdStray;
                }

                furthestStray = Math.Max(furthestStray, holdStray);
            }
        }

        return furthestStray;
    }
}
