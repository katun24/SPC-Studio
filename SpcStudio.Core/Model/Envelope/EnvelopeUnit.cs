namespace SpcStudio.Model;

// One stretch of a channel played under an envelope override: a start, a length, and the three bytes the DSP reads across the whole of it.
public readonly record struct EnvelopeUnit(int StartTick, int LengthTicks, EnvelopeState State) : ILaneUnit<EnvelopeUnit>
{
    // The same unit over a different stretch, carrying the same override.
    public EnvelopeUnit WithSpan(int startTick, int lengthTicks)
    {
        return this with { StartTick = startTick, LengthTicks = lengthTicks };
    }

    // Adds a unit to the end of a list in tick order, lengthening the last one instead where it ends where the new one starts and carries the same override.
    public static void AppendJoining(List<EnvelopeUnit> units, EnvelopeUnit unit)
    {
        if (units.Count > 0 && units[^1].EndTick() == unit.StartTick && units[^1].State == unit.State)
        {
            units[^1] = units[^1] with { LengthTicks = unit.EndTick() - units[^1].StartTick };
            return;
        }

        units.Add(unit);
    }

    // The units that make a channel play a given envelope: one over each stretch between the boundary ticks where that envelope is not the instrument's
    // own, the last stretch running to the song's end or a tick past its start where that lies beyond it.
    public static List<EnvelopeUnit> WherePlayedDiffers(IReadOnlyList<int> boundaryTicks, int songEndTick, Func<int, EnvelopeState> playedStateAt, EnvelopeMap instrumentTimeline)
    {
        var units = new List<EnvelopeUnit>();
        if (boundaryTicks.Count == 0)
        {
            return units;
        }

        int lastStretchEndTick = Math.Max(songEndTick, boundaryTicks[^1] + 1);
        for (int boundaryIndex = 0; boundaryIndex < boundaryTicks.Count; boundaryIndex++)
        {
            int stretchStartTick = boundaryTicks[boundaryIndex];
            int stretchEndTick = boundaryIndex + 1 < boundaryTicks.Count ? boundaryTicks[boundaryIndex + 1] : lastStretchEndTick;
            var playedState = playedStateAt(stretchStartTick);
            if (stretchEndTick <= stretchStartTick || playedState == instrumentTimeline.InstrumentEnvelopeAt(stretchStartTick))
            {
                continue;
            }

            AppendJoining(units, new EnvelopeUnit(stretchStartTick, stretchEndTick - stretchStartTick, playedState));
        }

        return units;
    }
}
