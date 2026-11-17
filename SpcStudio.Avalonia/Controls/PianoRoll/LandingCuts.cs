namespace SpcStudio.Controls;

// What a group about to land would cut away, on the roll and on a lane of units alike: the group is laid down in order, so each member cuts what stands
// under it and the members laid down before it.
public static class LandingCuts
{
    // Every member a landing member would cut, paired with it: for each landing member reaching into a horizontal range, the standing members starting
    // before it ends, and the landing members before it in the group that share its row.
    public static IEnumerable<(T Cut, T Cutter)> Of<T>(IReadOnlyList<T> landing, Func<T, (int StartTick, int EndTick)> spanOf, Func<T, IEnumerable<T>> standingUnder,
        Func<T, T, bool> doShareRow, double left, double right)
    {
        for (int landingIndex = 0; landingIndex < landing.Count; landingIndex++)
        {
            var cutter = landing[landingIndex];
            var (cutterStartTick, cutterEndTick) = spanOf(cutter);
            if (RollMetrics.XOfTick(cutterStartTick) > right || RollMetrics.XOfTick(cutterEndTick) < left)
            {
                continue;
            }

            foreach (var standing in standingUnder(cutter))
            {
                if (spanOf(standing).StartTick >= cutterEndTick)
                {
                    break;
                }

                yield return (standing, cutter);
            }

            for (int earlierIndex = 0; earlierIndex < landingIndex; earlierIndex++)
            {
                if (doShareRow(landing[earlierIndex], cutter))
                {
                    yield return (landing[earlierIndex], cutter);
                }
            }
        }
    }
}
