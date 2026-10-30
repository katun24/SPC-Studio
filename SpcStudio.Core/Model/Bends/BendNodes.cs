using System.Collections.Immutable;

namespace SpcStudio.Model;

// A note's bend nodes as a list of draggable joints - its start and its end, every start and end of a $DD ramp, and the level nodes the note keeps beside
// them - and the note that results from editing one.
public static class BendNodes
{
    // The nodes of a note in time order - opening on its start and closing on its end or on the end of a ramp past it - or empty where it carries none.
    public static ImmutableArray<BendNode> NodesOf(Note note)
    {
        var points = BendPath.PointsOf(note);
        var rampNodes = RampNodesOf(points);
        var nodesInTickOrder = rampNodes.Concat(LevelNodesOf(note, points, rampNodes)).OrderBy(node => node.Tick).ToList();
        if (nodesInTickOrder.Count == 0)
        {
            return [];
        }

        if (nodesInTickOrder[0].Tick > 0)
        {
            nodesInTickOrder.Insert(0, new BendNode(0, note.Semitone));
        }

        if (nodesInTickOrder[^1].Tick < note.LengthTicks)
        {
            nodesInTickOrder.Add(new BendNode(note.LengthTicks, PathRowAt(points, note.Semitone, note.LengthTicks).Semitone));
        }

        return [.. nodesInTickOrder];
    }

    // Whether a tick from a note's start is one a node can stand on: the note's start, its end or anywhere between.
    public static bool IsTickOnNote(int tick, int noteLengthTicks)
    {
        return tick >= 0 && tick <= noteLengthTicks;
    }

    // The ticks from a note's start a node can be moved between: a tick clear of the node either side, the start node free to run left to the song's own start and the last node free to run right.
    public static (int Lowest, int Highest) TickRangeOf(Note note, int nodeIndex)
    {
        return TickRangeAmong(note, NodesOf(note), nodeIndex);
    }

    // Whether a node stands beside a level stretch too long for a ramp, which holds it on its row.
    public static bool IsNodeHeldOnRow(Note note, int nodeIndex)
    {
        if (!TryGetNodes(note, nodeIndex, out var nodes))
        {
            return false;
        }

        return IsLevelStretchTooLongForRamp(nodes, nodeIndex - 1) || IsLevelStretchTooLongForRamp(nodes, nodeIndex);
    }

    // The note that results from dragging a node to a tick from the note's start and a semitone - along its row alone where that leaves it held on its row
    // - having been as far left as a tick since the press, or null where the drag cannot take it there.
    public static Note? WithNodeDragged(Note note, int nodeIndex, int tick, int semitone, int leftmostTick)
    {
        if (!TryGetNodes(note, nodeIndex, out var nodes))
        {
            return null;
        }

        if (WithNodeAt(note, nodeIndex, tick, nodes[nodeIndex].Semitone, leftmostTick) is Note movedAlongRow && IsNodeHeldOnRow(movedAlongRow, nodeIndex))
        {
            return movedAlongRow;
        }

        return WithNodeAt(note, nodeIndex, tick, semitone, leftmostTick);
    }

    // The note that results from putting one node at a tick from the note's start and a semitone, or null where the move cannot be made.
    public static Note? WithNodeMoved(Note note, int nodeIndex, int tick, int semitone)
    {
        return WithNodeAt(note, nodeIndex, tick, semitone, tick);
    }

    // The note that results from taking one node off, the path running straight from the vertex in front of it to the one behind it - or, for the start or the end of a note holding no other node, the
    // note left level at the other one's pitch - or null where the start or the end has others beside it or the chain cannot do without the node.
    public static Note? WithNodeRemoved(Note note, int nodeIndex)
    {
        if (!TryGetNodes(note, nodeIndex, out var nodes))
        {
            return null;
        }

        if (nodeIndex > 0 && nodeIndex < nodes.Length - 1)
        {
            var keptNodes = nodes.ToList();
            keptNodes.RemoveAt(nodeIndex);
            return WithPathThrough(note, keptNodes, note.StartTick, note.LengthTicks);
        }

        if (nodes.Length > 2)
        {
            return null;
        }

        return note with { Semitone = nodes[nodes.Length - 1 - nodeIndex].Semitone, Bends = [], FlatBendNodeTicks = [] };
    }

    // The note that results from putting a new node on a note's path at a tick from its start, on the row nearest the path there, with the new node's
    // index, or null where the tick is outside the note, already holds a node, or leaves a ramp the driver has no bytes for. A note with no nodes gets one
    // on its start and one on its end with it, and a node put on a note's end cuts off a ramp running past it.
    public static (Note Note, int NodeIndex)? WithNodeInserted(Note note, int tick)
    {
        var nodes = NodesOf(note);
        if (!IsTickOnNote(tick, note.LengthTicks) || nodes.Any(node => node.Tick == tick))
        {
            return null;
        }

        // The nodes kept beside the new one, those on or past the note's end dropped where the new one goes on the end.
        var insertedNodes = nodes.Where(node => tick < note.LengthTicks || node.Tick < note.LengthTicks).ToList();
        insertedNodes.Add(new BendNode(tick, PathRowAt(BendPath.PointsOf(note), note.Semitone, tick).Semitone));
        if (nodes.IsEmpty)
        {
            insertedNodes.Add(new BendNode(0, note.Semitone));
            insertedNodes.Add(new BendNode(note.LengthTicks, note.Semitone));
        }

        insertedNodes = [.. insertedNodes.DistinctBy(node => node.Tick).OrderBy(node => node.Tick)];
        if (WithPathThrough(note, insertedNodes, note.StartTick, note.LengthTicks) is not Note insertedNote)
        {
            return null;
        }

        return (insertedNote, insertedNodes.FindIndex(node => node.Tick == tick));
    }

    // The note that results from putting one node at a tick and a semitone, the drag having been as far left as a tick: the start node moving the note's
    // start, the node on its end moving the end, and a node past the end cutting the note wherever it has been back to the end or inside it.
    private static Note? WithNodeAt(Note note, int nodeIndex, int tick, int semitone, int leftmostTick)
    {
        if (!TryGetNodes(note, nodeIndex, out var nodes))
        {
            return null;
        }

        var (lowestTick, highestTick) = TickRangeAmong(note, nodes, nodeIndex);
        tick = Math.Clamp(tick, lowestTick, highestTick);
        semitone = Song.SemitoneRange.Clamp(semitone);

        var movedNodes = nodes.ToList();
        movedNodes[nodeIndex] = new BendNode(tick, semitone);
        if (nodeIndex == 0)
        {
            for (int index = 0; index < movedNodes.Count; index++)
            {
                movedNodes[index] = movedNodes[index] with { Tick = movedNodes[index].Tick - tick };
            }

            return WithPathThrough(note, movedNodes, note.StartTick + tick, note.LengthTicks - tick);
        }

        // The note's length once the node has moved: the node's tick where it is on the end, and as far left as a node past the end has been inside it.
        int lengthTicks = note.LengthTicks;
        if (nodes[nodeIndex].Tick == note.LengthTicks)
        {
            lengthTicks = tick;
        }
        else if (nodes[nodeIndex].Tick > note.LengthTicks)
        {
            lengthTicks = Math.Min(note.LengthTicks, Math.Max(leftmostTick, lowestTick));
        }

        return WithPathThrough(note, movedNodes, note.StartTick, lengthTicks);
    }

    // The ticks a node in a list of a note's nodes can be moved between.
    private static (int Lowest, int Highest) TickRangeAmong(Note note, ImmutableArray<BendNode> nodes, int nodeIndex)
    {
        if (nodeIndex == 0)
        {
            return (-note.StartTick, Math.Min(nodes[1].Tick, note.LengthTicks) - 1);
        }

        int highestTick = nodeIndex < nodes.Length - 1 ? nodes[nodeIndex + 1].Tick - 1 : int.MaxValue;
        return (nodes[nodeIndex - 1].Tick + 1, highestTick);
    }

    // The nodes a chain puts on its path, from the path's vertices: every ramp's start and end, which leaves out a level stretch at either end of the path.
    private static ImmutableArray<BendNode> RampNodesOf(ImmutableArray<BendPoint> points)
    {
        if (points.IsEmpty)
        {
            return [];
        }

        int firstRampVertexIndex = points[1].Semitone == points[0].Semitone ? 1 : 0;
        int lastRampVertexIndex = points[^1].Semitone == points[^2].Semitone ? points.Length - 2 : points.Length - 1;
        return [.. points.Skip(firstRampVertexIndex).Take(lastRampVertexIndex - firstRampVertexIndex + 1).Select(point => new BendNode(point.Tick, point.Semitone))];
    }

    // The level nodes a note keeps that still stand inside it on a level stretch of its path, away from every ramp node.
    private static List<BendNode> LevelNodesOf(Note note, ImmutableArray<BendPoint> points, ImmutableArray<BendNode> rampNodes)
    {
        var levelNodes = new List<BendNode>(note.FlatBendNodeTicks.Length);
        foreach (int tick in note.FlatBendNodeTicks)
        {
            bool isTickTaken = rampNodes.Any(node => node.Tick == tick) || levelNodes.Any(node => node.Tick == tick);
            if (!IsTickOnNote(tick, note.LengthTicks) || isTickTaken)
            {
                continue;
            }

            var (semitone, isLevel) = PathRowAt(points, note.Semitone, tick);
            if (isLevel)
            {
                levelNodes.Add(new BendNode(tick, semitone));
            }
        }

        return levelNodes;
    }

    // The row a path is on at a tick - the nearest where a ramp is moving it there, a half row rounding away from where the ramp started - and whether the path is level there.
    private static (int Semitone, bool IsLevel) PathRowAt(ImmutableArray<BendPoint> points, int noteSemitone, int tick)
    {
        for (int index = 1; index < points.Length; index++)
        {
            var (from, to) = (points[index - 1], points[index]);
            if (tick > to.Tick)
            {
                continue;
            }

            if (from.Semitone == to.Semitone)
            {
                return (from.Semitone, true);
            }

            double tickShareThroughRamp = (double)(tick - from.Tick) / (to.Tick - from.Tick);
            return (from.Semitone + (int)Math.Round((to.Semitone - from.Semitone) * tickShareThroughRamp, MidpointRounding.AwayFromZero), false);
        }

        return (points.IsEmpty ? noteSemitone : points[^1].Semitone, true);
    }

    // Whether the stretch from the node at an index to the one after it is level and longer than a ramp can be.
    private static bool IsLevelStretchTooLongForRamp(ImmutableArray<BendNode> nodes, int fromIndex)
    {
        if (fromIndex < 0 || fromIndex + 1 >= nodes.Length)
        {
            return false;
        }

        var (from, to) = (nodes[fromIndex], nodes[fromIndex + 1]);
        return from.Semitone == to.Semitone && to.Tick - from.Tick > PitchBend.MaxLengthTicks;
    }

    // A note's nodes, answering whether there is one at an index.
    private static bool TryGetNodes(Note note, int nodeIndex, out ImmutableArray<BendNode> nodes)
    {
        nodes = NodesOf(note);
        return nodeIndex >= 0 && nodeIndex < nodes.Length;
    }

    // The note on a start tick and a length with its path through a list of nodes opening on its start: its written pitch the first node's, its chain derived from the nodes - a level segment being a
    // delay and a sloped one a ramp - and every node no ramp starts or ends on kept as a level node, the start and the end only where no other node would bring them; or null where a ramp is outside
    // the lengths the driver takes or starts after the note has ended, or a level node stands past the note's end.
    private static Note? WithPathThrough(Note note, IReadOnlyList<BendNode> nodes, int startTick, int lengthTicks)
    {
        var chain = ImmutableArray.CreateBuilder<PitchBend>(nodes.Count);
        int delayTicks = 0;
        for (int nodeIndex = 1; nodeIndex < nodes.Count; nodeIndex++)
        {
            int ticksFromPreviousNode = nodes[nodeIndex].Tick - nodes[nodeIndex - 1].Tick;
            if (nodes[nodeIndex].Semitone == nodes[nodeIndex - 1].Semitone)
            {
                delayTicks += ticksFromPreviousNode;
                continue;
            }

            if (!PitchBend.LengthTicksRange.Contains(ticksFromPreviousNode))
            {
                return null;
            }

            chain.Add(new PitchBend(delayTicks, ticksFromPreviousNode, nodes[nodeIndex].Semitone - nodes[nodeIndex - 1].Semitone));
            delayTicks = 0;
        }

        if (!AreAllRampsArmedInsideNote(chain, lengthTicks))
        {
            return null;
        }

        // The chain through the nodes, its last ramp still running on to the note's end where the note's own did and it still ends there.
        var derivedBends = note.Bends is [.., { IsEndless: true }] ? NoteSweep.WithEndlessTail(chain.ToImmutable(), lengthTicks) : chain.ToImmutable();

        // The note on its new start and length, playing the chain through the nodes.
        var shapedNote = note with
        {
            StartTick = startTick,
            LengthTicks = lengthTicks,
            Semitone = nodes[0].Semitone,
            Bends = derivedBends,
            FlatBendNodeTicks = [],
        };

        var rampNodeTicks = RampNodesOf(BendPath.PointsOf(shapedNote)).Select(node => node.Tick).ToHashSet();
        var levelNodeTicks = nodes.Select(node => node.Tick).Where(tick => !rampNodeTicks.Contains(tick)).ToList();
        if (levelNodeTicks.Any(tick => !IsTickOnNote(tick, lengthTicks)))
        {
            return null;
        }

        // The level nodes between the note's two ends, without which the start and the end are nodes only where they are kept themselves.
        var innerLevelNodeTicks = levelNodeTicks.Where(tick => tick > 0 && tick < lengthTicks).ToList();

        bool areEndsBroughtByOthers = rampNodeTicks.Count > 0 || innerLevelNodeTicks.Count > 0;
        return shapedNote with { FlatBendNodeTicks = [.. areEndsBroughtByOthers ? innerLevelNodeTicks : levelNodeTicks] };
    }

    // Whether every ramp of a chain starts on a tick a note of that length is still sounding on.
    private static bool AreAllRampsArmedInsideNote(IEnumerable<PitchBend> chain, int noteLengthTicks)
    {
        int armTick = 0;
        foreach (var bend in chain)
        {
            armTick += bend.DelayTicks;
            if (armTick >= noteLengthTicks)
            {
                return false;
            }

            armTick += bend.LengthTicks;
        }

        return true;
    }
}
