using SpcStudio.Model;

namespace SpcStudio.Import;

// One value a MIDI channel holds over time - its loudness, pan, bend or vibrato depth - as the value from each tick it moved at, starting at tick 0.
public sealed class MidiCurve
{
    private readonly List<(long Tick, double Value)> _points = [];

    // A curve holding a value from tick 0.
    public MidiCurve(double startValue)
    {
        _points.Add((0, startValue));
    }

    // Sets the value from a tick on, no earlier than the last tick set, replacing a value already set on that tick and keeping nothing that moves no value.
    public void Set(long tick, double value)
    {
        if (_points[^1].Tick == tick)
        {
            if (_points.Count == 1)
            {
                _points[0] = (tick, value);
                return;
            }

            _points.RemoveAt(_points.Count - 1);
        }

        if (_points[^1].Value == value)
        {
            return;
        }

        _points.Add((tick, value));
    }

    // The value in force at a tick.
    public double ValueAt(long tick)
    {
        return _points[PointsAtOrBefore(tick) - 1].Value;
    }

    // The value in force at a tick, then each move strictly after it and before another tick, in order.
    public IEnumerable<(long Tick, double Value)> MovesFrom(long startTick, long endTick)
    {
        int firstPointAfterStart = PointsAtOrBefore(startTick);
        yield return (startTick, _points[firstPointAfterStart - 1].Value);
        for (int pointIndex = firstPointAfterStart; pointIndex < _points.Count && _points[pointIndex].Tick < endTick; pointIndex++)
        {
            yield return _points[pointIndex];
        }
    }

    // How many points stand at or before a tick, which is never fewer than the one at tick 0.
    private int PointsAtOrBefore(long tick)
    {
        return Math.Max(SortedSearch.CountLeading(_points, point => point.Tick <= tick), 1);
    }
}
