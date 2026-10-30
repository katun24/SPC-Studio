using System.Collections.Immutable;

namespace SpcStudio.Model;

// A note's $DD chain as the pitch path it plays: the note's own semitone, then a vertex at every place the path changes direction.
public static class BendPath
{
    // The path a note plays from its start to the end of its length or of its chain, whichever is later, or empty where it carries no bend.
    public static ImmutableArray<BendPoint> PointsOf(Note note)
    {
        if (note.Bends.IsEmpty)
        {
            return [];
        }

        var points = ImmutableArray.CreateBuilder<BendPoint>(note.Bends.Length * 2 + 2);
        int semitone = note.Semitone;
        int tick = 0;
        points.Add(new BendPoint(tick, semitone));
        foreach (var bend in note.Bends)
        {
            int rampStartTick = tick + bend.DelayTicks;
            if (rampStartTick > tick)
            {
                points.Add(new BendPoint(rampStartTick, semitone));
            }

            tick = rampStartTick + bend.LengthTicks;
            semitone += bend.OffsetSemitones;
            points.Add(new BendPoint(tick, semitone));
        }

        if (note.LengthTicks > tick)
        {
            points.Add(new BendPoint(note.LengthTicks, semitone));
        }

        return points.ToImmutable();
    }
}
