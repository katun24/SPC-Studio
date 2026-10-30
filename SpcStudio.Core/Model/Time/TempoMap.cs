namespace SpcStudio.Model;

// The song's tempo as a function of position: a base tempo from tick 0 and the changes laid over it, converting between ticks and seconds.
public sealed class TempoMap
{
    private readonly Row[] _rows;

    // The tempo before the song's first tick, which is what a fade at tick 0 ramps from.
    private readonly int _baseTempo;

    private TempoMap(Row[] rows, int baseTempo)
    {
        _rows = rows;
        _baseTempo = baseTempo;
    }

    // The tempo map of a song.
    public static TempoMap For(Song song)
    {
        return Build(song.BaseTempo, LaneRegions.TempoCommands(song));
    }

    // A map with no changes, which is one row at a tempo.
    public static TempoMap Flat(int tempo)
    {
        return Build(tempo, []);
    }

    // The tempo in force at a tick, the tick's own ramp step included, and the base before the song's first tick.
    public int TempoAt(double tick)
    {
        return tick < 0 ? _baseTempo : _rows[RowIndexAt(tick)].Tempo;
    }

    // When a tick falls, in seconds from the start of the song.
    public double SecondsAt(double tick)
    {
        var rowContainingTick = _rows[RowIndexAt(tick)];
        return rowContainingTick.Seconds + SnesTiming.TicksToSeconds(tick - rowContainingTick.Tick, rowContainingTick.Tempo);
    }

    // Which tick a time falls on, carrying on at the last row's tempo past the end of it.
    public double TickAt(double seconds)
    {
        var rowContainingTime = _rows[LastRowIndexWhere(candidate => candidate.Seconds <= seconds)];
        return rowContainingTime.Tick + (seconds - rowContainingTime.Seconds) * SnesTiming.TicksPerSecond(rowContainingTime.Tempo);
    }

    // The ticks strictly inside a span that a tempo command sits at.
    public IEnumerable<int> ChangesWithin(int startTick, int endTick)
    {
        foreach (var row in _rows)
        {
            if (row.HasCommand && row.Tick > startTick && row.Tick < endTick)
            {
                yield return row.Tick;
            }
        }
    }

    // The map a base tempo and a stream of tempo commands come to, a ramp expanded into a row at every tick it moves the tempo on.
    private static TempoMap Build(int baseTempo, IEnumerable<LaneCommand> stream)
    {
        var tempoCommands = stream.ToList();
        var rows = new List<Row>();
        double reachedSeconds = 0;
        int reachedTick = 0;

        // The tempo byte as the driver fades it.
        var tempoFade = SnesFade.HeldAt(baseTempo);

        // The first command still to walk, past one at tick 0 that folds into the base's own row.
        int firstCommandIndex = 0;
        if (tempoCommands.Count > 0 && tempoCommands[0].Tick == 0)
        {
            if (tempoCommands[0].Fades)
            {
                tempoFade = tempoFade.FadingTo(tempoCommands[0].FadeTo, tempoCommands[0].FadeTicks);
            }

            firstCommandIndex = 1;
        }

        tempoFade = tempoFade.Advanced(1);
        rows.Add(new Row(0, tempoFade.Value, 0, true));
        for (int index = firstCommandIndex; index < tempoCommands.Count; index++)
        {
            var command = tempoCommands[index];
            WalkRampTo(command.Tick - 1);
            reachedSeconds += SnesTiming.TicksToSeconds(1, tempoFade.Value);
            reachedTick = command.Tick;
            if (command.SetValue is int setTempo)
            {
                tempoFade = tempoFade.WithValueSet(setTempo);
            }

            if (command.Fades)
            {
                tempoFade = tempoFade.FadingTo(command.FadeTo, command.FadeTicks);
            }

            tempoFade = tempoFade.Advanced(1);
            rows.Add(new Row(reachedTick, tempoFade.Value, reachedSeconds, true));
        }

        WalkRampTo(reachedTick + tempoFade.TicksLeft);
        return new TempoMap([.. rows], baseTempo);

        // Walks to a tick with no command between here and there, adding a row wherever a ramp moves the tempo.
        void WalkRampTo(int targetTick)
        {
            while (reachedTick < targetTick)
            {
                if (!tempoFade.IsFading)
                {
                    reachedSeconds += SnesTiming.TicksToSeconds(targetTick - reachedTick, tempoFade.Value);
                    reachedTick = targetTick;
                    return;
                }

                reachedSeconds += SnesTiming.TicksToSeconds(1, tempoFade.Value);
                reachedTick++;
                tempoFade = tempoFade.Stepped();
                if (tempoFade.Value != rows[^1].Tempo)
                {
                    rows.Add(new Row(reachedTick, tempoFade.Value, reachedSeconds, false));
                }
            }
        }
    }

    // The last row starting at or before a tick.
    private int RowIndexAt(double tick)
    {
        return LastRowIndexWhere(row => row.Tick <= tick);
    }

    // The last row a test holds for, the test holding for every row up to some point and for none after, or the first row where it holds for none.
    private int LastRowIndexWhere(Func<Row, bool> isAtOrBefore)
    {
        return Math.Max(0, SortedSearch.CountLeading(_rows, isAtOrBefore) - 1);
    }

    // One stretch of constant tempo: its first tick, the tempo its ticks run at, the time the first of them falls at, and whether a driver command sits there rather than a ramp's step.
    private readonly record struct Row(int Tick, int Tempo, double Seconds, bool HasCommand);
}
