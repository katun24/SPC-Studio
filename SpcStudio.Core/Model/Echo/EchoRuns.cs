namespace SpcStudio.Model;

// A song's echo runs read as the driver sees them: where the echo starts, where it stops, and where it merely changes settings without stopping.
public static class EchoRuns
{
    // The stretches the runs cover, in tick order, with runs that touch merged into one.
    public static IEnumerable<(int StartTick, int EndTick)> CoveredStretches(IReadOnlyList<EchoRun> runs)
    {
        int stretchStartTick = -1;
        int stretchEndTick = -1;
        foreach (var run in runs)
        {
            if (stretchStartTick < 0)
            {
                (stretchStartTick, stretchEndTick) = (run.StartTick, run.EndTick());
                continue;
            }

            if (run.StartTick <= stretchEndTick)
            {
                stretchEndTick = Math.Max(stretchEndTick, run.EndTick());
                continue;
            }

            yield return (stretchStartTick, stretchEndTick);
            (stretchStartTick, stretchEndTick) = (run.StartTick, run.EndTick());
        }

        if (stretchStartTick >= 0)
        {
            yield return (stretchStartTick, stretchEndTick);
        }
    }

    // Every tick an $F1 has to be written at: where a covered stretch begins, and where one run gives way to another with different settings to state.
    public static IEnumerable<int> SettingsTicks(IReadOnlyList<EchoRun> runs)
    {
        foreach (var (run, joinedPreviousRun) in RunsWithJoinedPrevious(runs))
        {
            if (joinedPreviousRun is not { } before || DoesSeamNeedSettings(before.State, run.State))
            {
                yield return run.StartTick;
            }
        }
    }

    // Every tick an $F5 has to be written at: where a run moves to a filter of the song's own, and where an $F1 is written while one is in force.
    public static IEnumerable<int> FilterTicks(IReadOnlyList<EchoRun> runs)
    {
        var settingsTicks = SettingsTicks(runs).ToHashSet();
        foreach (var (run, joinedPreviousRun) in RunsWithJoinedPrevious(runs))
        {
            bool hasFilterMoved = joinedPreviousRun is not { } before || before.State.Filter != run.State.Filter;
            if (SnesEcho.IsCustomFilter(run.State.Filter) && (hasFilterMoved || settingsTicks.Contains(run.StartTick)))
            {
                yield return run.StartTick;
            }
        }
    }

    // Every tick an $F0 has to be written at, which is the end of each covered stretch.
    public static IEnumerable<int> StopTicks(IReadOnlyList<EchoRun> runs)
    {
        return CoveredStretches(runs).Select(stretch => stretch.EndTick);
    }

    // Every tick an $EF has to be written at, which is the start of each covered stretch.
    public static IEnumerable<int> StartTicks(IReadOnlyList<EchoRun> runs)
    {
        return CoveredStretches(runs).Select(stretch => stretch.StartTick);
    }

    // Each run with the run in front of it where the two touch, which makes their meeting a seam inside one stretch, or null where it opens a stretch.
    private static IEnumerable<(EchoRun Run, EchoRun? JoinedPreviousRun)> RunsWithJoinedPrevious(IReadOnlyList<EchoRun> runs)
    {
        EchoRun? previousRun = null;
        foreach (var run in runs)
        {
            bool isJoinedToPrevious = previousRun is { } before && before.EndTick() >= run.StartTick;
            yield return (run, isJoinedToPrevious ? previousRun : null);
            previousRun = run;
        }
    }

    // Whether a seam inside one stretch has to write an $F1, which a move of the delay, of the feedback or to a preset filter does.
    private static bool DoesSeamNeedSettings(EchoUnitState before, EchoUnitState after)
    {
        return before.Delay != after.Delay
            || before.Feedback != after.Feedback
            || (before.Filter != after.Filter && !SnesEcho.IsCustomFilter(after.Filter));
    }
}
