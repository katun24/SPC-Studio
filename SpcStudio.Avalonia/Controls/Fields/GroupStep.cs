using SpcStudio.Model;

namespace SpcStudio.Controls;

// A step taken by a whole selection at once, each member moving by the same amount.
public static class GroupStep
{
    // The largest part of a step every member can take inside its own range, never turned past zero.
    public static int ClampedToEveryMember(int step, IEnumerable<(int Value, ValueRange Range)> members)
    {
        foreach (var (value, range) in members)
        {
            step = step > 0 ? Math.Max(0, Math.Min(step, range.Max - value)) : Math.Min(0, Math.Max(step, range.Min - value));
        }

        return step;
    }

    // A move of a group held so that the group, its earliest member starting at a tick, is not carried off the front of the song.
    public static int HeldOffSongStart(int startDelta, int earliestTick)
    {
        return Math.Max(startDelta, -earliestTick);
    }
}
