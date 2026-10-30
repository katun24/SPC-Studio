using System.Collections.Immutable;
using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio.Import;

// Finds the rhythm a dump was played to and where it changes: the period read a few seconds at a time from when the notes started, the readings joined
// into stretches, each stretch measured on its own moments, and only then each spelled as a step and a tempo the driver can play.
public static class SpcGridFit
{
    public const double MinBelievableFitScore = 0.5;
    private const double ShortestStepMs = 15;
    private const double LongestStepMs = 600;
    private const double ShortestStepFrames = ShortestStepMs * DspPoint.PointsPerMs;
    private const double LongestStepFrames = LongestStepMs * DspPoint.PointsPerMs;
    private const double WindowSeconds = 4;
    private const double WindowFrames = WindowSeconds * DspPoint.SampleRate;
    private const double HopSeconds = 1;
    private const double HopFrames = HopSeconds * DspPoint.SampleRate;
    private const int MinWindowMoments = 12;
    private const double MinWindowFitScore = 0.6;
    private const double WindowCloseEnough = 0.85;
    private const double CloseEnough = 0.97;
    private const double SweepDensity = 4;
    private const double WindowRefineBy = 0.02;
    private const int WindowRefineSteps = 200;
    private const double RefineBy = 0.01;
    private const int RefineSteps = 200;
    private const int RampRefineSteps = 40;
    private const int RefinePasses = 2;
    private const int RunReferenceCount = 3;
    private const int MinSlidingWindows = 3;
    private const double SplitCost = 40;
    private const double SlideCost = 30;
    private const double MostDisagreement = 16;
    private const double ReadingPrecision = 0.002;
    private const double LongPeriodLooseness = 0.1;
    private const double LeastSlide = 0.01;
    private const double PauseSteps = 4;
    private const double PreferredBpm = 120;
    private const double SpellingSlack = 0.02;

    private static readonly int[] RatioParts = [1, 2, 3, 4, 6, 8, 12, 16, 24];

    // Every step length a period may be called, shortest first, each with whether it is a triplet.
    private static readonly (int StepTicks, bool IsTriplet)[] Spellings =
    [
        (SnesTiming.TicksPerSixteenthNote / 2, false),
        (SnesTiming.TicksPerEighthNote / 3, true),
        (SnesTiming.TicksPerSixteenthNote, false),
        (SnesTiming.TicksPerQuarterNote / 3, true),
        (SnesTiming.TicksPerEighthNote, false),
    ];

    // Every ratio of two of the ratio parts, which are the multiples one period may be of another and still be the same speed.
    private static readonly double[] Ratios = RatioParts.SelectMany(over => RatioParts.Select(under => (double)over / under)).Distinct().Order().ToArray();

    // The rhythm the onsets were played to and every place it changes, a slide in the last stretch arriving where the song ends.
    public static SpcGrid FindGrid(IReadOnlyList<long> onsets, long endFrame, Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        // The onsets in order, each frame once.
        IReadOnlyList<long> moments = [.. onsets.Distinct().Order()];
        if (moments.Count < 2)
        {
            return GridWithNoRhythm();
        }

        var windows = MeasureWindows(moments, progress, cancel);
        var runs = JoinIntoRuns(windows, cancel);
        if (runs.Count == 0)
        {
            var whole = MeasureWindow([.. moments], moments[0]);
            if (whole.PeriodFrames <= 0)
            {
                return GridWithNoRhythm();
            }

            runs = [new Run(0, windows.Count - 1, whole.PeriodFrames, 0)];
        }

        return LayStretches(moments, endFrame, windows, runs, cancel);
    }

    // How many steps lie a number of frames past a point where one step lasts a period and the rhythm grows by a factor a frame.
    internal static double StepsAfter(double periodFrames, double growthPerFrame, double frames)
    {
        return growthPerFrame == 0 ? frames / periodFrames : double.ExpM1(growthPerFrame * frames) / (growthPerFrame * periodFrames);
    }

    // Every window's reading, in order, reporting each as it is measured.
    private static List<Window> MeasureWindows(IReadOnlyList<long> moments, Action<int, int>? progress, CancellationToken cancel)
    {
        int windowCount = (int)Math.Floor(moments[^1] / HopFrames) + 1;
        var windows = new List<Window>(windowCount);
        progress?.Invoke(0, windowCount);
        int firstInside = 0;
        var inside = new List<long>();
        for (int at = 0; at < windowCount; at++)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Invoke(at, windowCount);
            double startFrame = at * HopFrames;
            while (firstInside < moments.Count && moments[firstInside] < startFrame)
            {
                firstInside++;
            }

            inside.Clear();
            for (int next = firstInside; next < moments.Count && moments[next] < startFrame + WindowFrames; next++)
            {
                inside.Add(moments[next]);
            }

            var windowReading = inside.Count >= MinWindowMoments ? MeasureWindow(inside, (long)startFrame) : (PeriodFrames: 0.0, FitScore: 0.0);
            windows.Add(new Window(startFrame, inside.Count, windowReading.PeriodFrames, windowReading.FitScore));
        }

        progress?.Invoke(windowCount, windowCount);
        return windows;
    }

    // The period that explains a few seconds of moments: swept over the whole range, the longest that comes near the best taken, and then refined.
    private static (double PeriodFrames, double FitScore) MeasureWindow(List<long> moments, long originFrame)
    {
        if (moments.Count < 2)
        {
            return (0, 0);
        }

        double spanFrames = Math.Max(moments[^1] - moments[0], 1);
        var sweptPeriods = new List<(double Value, double FitScore)>();
        double bestFitScore = 0;
        for (double period = ShortestStepFrames; period <= LongestStepFrames; period += period * period / (spanFrames * SweepDensity))
        {
            double fitScore = ScoreRhythm(moments, originFrame, period, 0).FitScore;
            sweptPeriods.Add((period, fitScore));
            bestFitScore = Math.Max(bestFitScore, fitScore);
        }

        double longestNearBestPeriod = LongestNearBest(sweptPeriods, bestFitScore, WindowCloseEnough, ShortestStepFrames);
        double bestPeriod = longestNearBestPeriod;
        double refinedFitScore = 0;
        for (int at = 0; at <= WindowRefineSteps; at++)
        {
            double triedPeriod = SweptAround(longestNearBestPeriod, WindowRefineBy, at, WindowRefineSteps);
            double fitScore = ScoreRhythm(moments, originFrame, triedPeriod, 0).FitScore;
            if (fitScore <= refinedFitScore)
            {
                continue;
            }

            refinedFitScore = fitScore;
            bestPeriod = triedPeriod;
        }

        return (bestPeriod, refinedFitScore);
    }

    // The latest value tried whose score comes near the best, the fallback where none does.
    private static double LongestNearBest(List<(double Value, double FitScore)> tried, double bestFitScore, double closeEnough, double fallback)
    {
        for (int at = tried.Count - 1; at >= 0; at--)
        {
            if (tried[at].FitScore >= bestFitScore * closeEnough)
            {
                return tried[at].Value;
            }
        }

        return fallback;
    }

    // One of the evenly spread values a sweep tries either side of a center, a fraction of it either way.
    private static double SweptAround(double center, double fractionEitherWay, int at, int stepCount)
    {
        return center * (1 - fractionEitherWay + SweepStepFraction(fractionEitherWay, stepCount) * at);
    }

    // How far apart the values a sweep tries stand, as a fraction of its center.
    private static double SweepStepFraction(double fractionEitherWay, int stepCount)
    {
        return 2 * fractionEitherWay / stepCount;
    }

    // The windows joined into the fewest runs that explain the readings, each at one speed or at one steady slide, solved over every prefix of the counted windows.
    private static List<Run> JoinIntoRuns(List<Window> windows, CancellationToken cancel)
    {
        var counting = new List<int>();
        for (int at = 0; at < windows.Count; at++)
        {
            if (windows[at].DoesCount)
            {
                counting.Add(at);
            }
        }

        if (counting.Count == 0)
        {
            return [];
        }

        int countingCount = counting.Count;

        // How far each counted reading is from the nearest multiple of each other one, as a log ratio.
        var apart = new double[countingCount, countingCount];
        for (int one = 0; one < countingCount; one++)
        {
            for (int other = 0; other < countingCount; other++)
            {
                double periodRatio = windows[counting[other]].PeriodFrames / windows[counting[one]].PeriodFrames;
                apart[one, other] = Math.Log(periodRatio / NearestRatio(periodRatio));
            }
        }

        var cheapestPrefixSplitCosts = new double[countingCount + 1];
        var cheapestSplitLastRunStarts = new int[countingCount + 1];
        var cheapestSplitLastRunShapes = new RunShape[countingCount + 1];
        for (int end = 1; end <= countingCount; end++)
        {
            cheapestPrefixSplitCosts[end] = double.MaxValue;
            for (int start = 0; start < end; start++)
            {
                cancel.ThrowIfCancellationRequested();
                var run = CheapestRunShape(windows, counting, apart, start, end - 1);
                double costWithThisRun = cheapestPrefixSplitCosts[start] + run.Cost + SplitCost;
                if (costWithThisRun >= cheapestPrefixSplitCosts[end])
                {
                    continue;
                }

                cheapestPrefixSplitCosts[end] = costWithThisRun;
                cheapestSplitLastRunStarts[end] = start;
                cheapestSplitLastRunShapes[end] = run;
            }
        }

        var runs = new List<Run>();
        for (int end = countingCount; end > 0; end = cheapestSplitLastRunStarts[end])
        {
            var run = cheapestSplitLastRunShapes[end];
            runs.Insert(0, new Run(counting[cheapestSplitLastRunStarts[end]], counting[end - 1], run.PeriodFrames, run.GrowthPerFrame));
        }

        for (int at = 0; at < runs.Count; at++)
        {
            int firstWindow = at == 0 ? 0 : runs[at - 1].LastWindow + 1;
            int lastWindow = at == runs.Count - 1 ? windows.Count - 1 : runs[at + 1].FirstWindow - 1;
            runs[at] = runs[at] with { FirstWindow = firstWindow, LastWindow = Math.Max(lastWindow, runs[at].LastWindow) };
        }

        return runs;
    }

    // The cheapest reading of a span of counted windows as one run, at one speed or at one steady slide, each of the best few readings tried as the one the rest are measured against.
    private static RunShape CheapestRunShape(List<Window> windows, List<int> counting, double[,] apart, int first, int last)
    {
        var cheapestShapeSoFar = new RunShape(double.MaxValue, 0, 0);
        var bestScoringReferences = Enumerable.Range(first, last - first + 1).OrderByDescending(at => windows[counting[at]].FitScore).Take(RunReferenceCount);
        int count = last - first + 1;
        var offInOrder = new double[count];

        // How far each reading is from the nearest multiple of the reference, as a log ratio.
        var off = new double[count];
        foreach (int reference in bestScoringReferences)
        {
            for (int at = 0; at < count; at++)
            {
                off[at] = apart[reference, first + at];
            }

            Array.Copy(off, offInOrder, count);
            Array.Sort(offInOrder);
            double medianOff = offInOrder[count / 2];
            double flatCost = 0;
            for (int at = 0; at < count; at++)
            {
                flatCost += DisagreementCost(off[at] - medianOff, windows[counting[first + at]]);
            }

            double period = windows[counting[reference]].PeriodFrames;
            if (flatCost < cheapestShapeSoFar.Cost)
            {
                cheapestShapeSoFar = new RunShape(flatCost, period * Math.Exp(medianOff), 0);
            }

            if (count < MinSlidingWindows)
            {
                continue;
            }

            if (SlidingRunShape(windows, counting, off, first, period) is { } sliding && sliding.Cost < cheapestShapeSoFar.Cost)
            {
                cheapestShapeSoFar = sliding;
            }
        }

        return cheapestShapeSoFar;
    }

    // A span of counted windows read as one steady slide, a straight line laid through the log periods over seconds, or null where the windows all stand at one moment.
    private static RunShape? SlidingRunShape(List<Window> windows, List<int> counting, double[] off, int first, double referencePeriod)
    {
        int count = off.Length;
        double meanTime = 0, meanOff = 0;
        for (int at = 0; at < count; at++)
        {
            meanTime += windows[counting[first + at]].MiddleSeconds;
            meanOff += off[at];
        }

        meanTime /= count;
        meanOff /= count;
        double across = 0, along = 0;
        for (int at = 0; at < count; at++)
        {
            double secondsFromMeanTime = windows[counting[first + at]].MiddleSeconds - meanTime;
            across += secondsFromMeanTime * secondsFromMeanTime;
            along += secondsFromMeanTime * (off[at] - meanOff);
        }

        if (across <= 0)
        {
            return null;
        }

        double logPeriodSlopePerSecond = along / across;
        double lineAtSecondZero = meanOff - logPeriodSlopePerSecond * meanTime;
        double slidingCost = SlideCost;
        for (int at = 0; at < count; at++)
        {
            var window = windows[counting[first + at]];
            slidingCost += DisagreementCost(off[at] - (lineAtSecondZero + logPeriodSlopePerSecond * window.MiddleSeconds), window);
        }

        return new RunShape(slidingCost, referencePeriod * Math.Exp(lineAtSecondZero), logPeriodSlopePerSecond / DspPoint.SampleRate * -1);
    }

    // What a reading this far out of its run costs, capped.
    private static double DisagreementCost(double off, Window window)
    {
        double windowReadPrecision = ReadingPrecision + LongPeriodLooseness * window.PeriodFrames / WindowFrames;
        double offInPrecisionUnits = off / windowReadPrecision;
        return Math.Min(offInPrecisionUnits * offInPrecisionUnits, MostDisagreement);
    }

    // The multiple one period may be of another that is nearest a ratio.
    private static double NearestRatio(double ratio)
    {
        double nearest = 1;
        double closest = double.MaxValue;
        foreach (double candidate in Ratios)
        {
            double logDistance = Math.Abs(Math.Log(ratio / candidate));
            if (logDistance >= closest)
            {
                continue;
            }

            closest = logDistance;
            nearest = candidate;
        }

        return nearest;
    }

    // The runs laid out as the song's stretches: each measured on its own moments, the boundary between two of them found, each measured again over the
    // moments it then holds, and each spelled and ticked.
    private static SpcGrid LayStretches(IReadOnlyList<long> moments, long endFrame, List<Window> windows, List<Run> runs, CancellationToken cancel)
    {
        // Each run measured on the moments plainly its own.
        var cores = new List<MeasuredStretch>();
        for (int at = 0; at < runs.Count; at++)
        {
            cancel.ThrowIfCancellationRequested();
            double coreFromFrame = at == 0 ? 0 : windows[runs[at - 1].LastWindow].StartFrame + WindowFrames;
            double coreUntilFrame = at == runs.Count - 1 ? double.MaxValue : windows[runs[at + 1].FirstWindow].StartFrame;
            var core = MomentsBetween(moments, coreFromFrame, coreUntilFrame);
            if (core.Count < 2)
            {
                core = MomentsBetween(moments, windows[runs[at].FirstWindow].StartFrame, windows[runs[at].LastWindow].StartFrame + WindowFrames);
            }

            if (core.Count < 2)
            {
                core = [.. moments];
            }

            cores.Add(MeasureRun(core, runs[at]));
        }

        var laterRunStartIndexes = new List<int>();
        for (int at = 1; at < runs.Count; at++)
        {
            cancel.ThrowIfCancellationRequested();
            laterRunStartIndexes.Add(BoundaryIndex(moments, windows, runs[at - 1], runs[at], cores[at - 1], cores[at], laterRunStartIndexes.Count > 0 ? laterRunStartIndexes[^1] : 0));
        }

        var remeasuredStretches = new List<MeasuredStretch>();
        var runMomentCounts = new List<int>();
        for (int at = 0; at < runs.Count; at++)
        {
            int firstMomentIndex = at == 0 ? 0 : laterRunStartIndexes[at - 1];
            cancel.ThrowIfCancellationRequested();
            int pastLastMomentIndex = at == runs.Count - 1 ? moments.Count : laterRunStartIndexes[at];
            var heldMoments = new List<long>();
            for (int index = firstMomentIndex; index < pastLastMomentIndex; index++)
            {
                heldMoments.Add(moments[index]);
            }

            remeasuredStretches.Add(heldMoments.Count < 2 ? cores[at] : Refined(heldMoments, cores[at].PeriodFrames, cores[at].GrowthPerFrame, cores[at].OriginFrame));
            runMomentCounts.Add(heldMoments.Count);
        }

        return SpellStretches(moments, endFrame, remeasuredStretches, laterRunStartIndexes, runMomentCounts);
    }

    // The moments at or after a frame and before another.
    private static List<long> MomentsBetween(IReadOnlyList<long> moments, double from, double until)
    {
        var inside = new List<long>();
        foreach (long moment in moments)
        {
            if (moment >= from && moment < until)
            {
                inside.Add(moment);
            }
        }

        return inside;
    }

    // A run measured on some of its moments: its reading refined, and then the longest multiple of that reading that comes near the best taken as its step.
    private static MeasuredStretch MeasureRun(List<long> moments, Run run)
    {
        double origin = moments[0];
        var refinedReading = Refined(moments, run.PeriodFrames * Math.Exp(-run.GrowthPerFrame * origin), run.GrowthPerFrame, origin);
        var triedMultiples = new List<(double Value, double FitScore)>();
        double bestFitScore = 0;
        foreach (double multiple in Ratios)
        {
            double multiplePeriod = refinedReading.PeriodFrames * multiple;
            if (multiplePeriod < ShortestStepFrames || multiplePeriod > LongestStepFrames)
            {
                continue;
            }

            double fitScore = ScoreRhythm(moments, refinedReading.OriginFrame, multiplePeriod, refinedReading.GrowthPerFrame).FitScore;
            triedMultiples.Add((multiple, fitScore));
            bestFitScore = Math.Max(bestFitScore, fitScore);
        }

        double chosenMultiple = LongestNearBest(triedMultiples, bestFitScore, CloseEnough, 1);
        var (chosenFitScore, chosenPhase) = ScoreRhythm(moments, refinedReading.OriginFrame, refinedReading.PeriodFrames * chosenMultiple, refinedReading.GrowthPerFrame);
        return new MeasuredStretch(refinedReading.OriginFrame, refinedReading.PeriodFrames * chosenMultiple, refinedReading.GrowthPerFrame, chosenFitScore, chosenPhase);
    }

    // A stretch's period refined over its moments, counted from its own first moment: along the period alone at one speed, and along both ends of a slide.
    private static MeasuredStretch Refined(List<long> moments, double periodFrames, double growthPerFrame, double originFrame)
    {
        double firstMomentPeriod = periodFrames * Math.Exp(-growthPerFrame * (moments[0] - originFrame));
        double origin = moments[0];
        if (growthPerFrame == 0)
        {
            return RefinedAtOneSpeed(moments, firstMomentPeriod, origin);
        }

        double spanFrames = Math.Max(moments[^1] - origin, 1);
        var sliding = RefinedSliding(moments, firstMomentPeriod, growthPerFrame, origin, spanFrames);
        if (Math.Abs(sliding.GrowthPerFrame * spanFrames) < LeastSlide)
        {
            return Refined(moments, firstMomentPeriod * Math.Exp(-growthPerFrame * spanFrames / 2), 0, origin);
        }

        return sliding;
    }

    // A period refined at one speed in two passes, the second across the step either side of the first's best.
    private static MeasuredStretch RefinedAtOneSpeed(List<long> moments, double period, double origin)
    {
        var bestSoFar = new MeasuredStretch(origin, period, 0, 0, 0);
        double fractionEitherWay = RefineBy;
        for (int pass = 0; pass < RefinePasses; pass++)
        {
            double around = bestSoFar.PeriodFrames;
            for (int at = 0; at <= RefineSteps; at++)
            {
                double triedPeriod = SweptAround(around, fractionEitherWay, at, RefineSteps);
                var (fitScore, phase) = ScoreRhythm(moments, origin, triedPeriod, 0);
                if (fitScore > bestSoFar.FitScore)
                {
                    bestSoFar = new MeasuredStretch(origin, triedPeriod, 0, fitScore, phase);
                }
            }

            fractionEitherWay = SweepStepFraction(fractionEitherWay, RefineSteps);
        }

        return bestSoFar;
    }

    // A slide refined along its opening and closing periods at once, in two passes, the second across the step either side of the first's best.
    private static MeasuredStretch RefinedSliding(List<long> moments, double period, double growthPerFrame, double origin, double span)
    {
        var bestSlideSoFar = new MeasuredStretch(origin, period, growthPerFrame, 0, 0);
        double fractionEitherWay = RefineBy;
        double opensAround = period;
        double closesAround = period * Math.Exp(-growthPerFrame * span);
        for (int pass = 0; pass < RefinePasses; pass++)
        {
            for (int start = 0; start <= RampRefineSteps; start++)
            {
                for (int end = 0; end <= RampRefineSteps; end++)
                {
                    double triedOpeningPeriod = SweptAround(opensAround, fractionEitherWay, start, RampRefineSteps);
                    double triedClosingPeriod = SweptAround(closesAround, fractionEitherWay, end, RampRefineSteps);
                    double triedGrowthPerFrame = Math.Log(triedOpeningPeriod / triedClosingPeriod) / span;
                    var (fitScore, phase) = ScoreRhythm(moments, origin, triedOpeningPeriod, triedGrowthPerFrame);
                    if (fitScore > bestSlideSoFar.FitScore)
                    {
                        bestSlideSoFar = new MeasuredStretch(origin, triedOpeningPeriod, triedGrowthPerFrame, fitScore, phase);
                    }
                }
            }

            opensAround = bestSlideSoFar.PeriodFrames;
            closesAround = bestSlideSoFar.PeriodFrames * Math.Exp(-bestSlideSoFar.GrowthPerFrame * span);
            fractionEitherWay = SweepStepFraction(fractionEitherWay, RampRefineSteps);
        }

        return bestSlideSoFar;
    }

    // The index of the later run's first moment: the widest pause where the two runs' windows overlap where it is long enough to be one, and otherwise the
    // split that puts each moment with the rhythm that explains it best.
    private static int BoundaryIndex(
        IReadOnlyList<long> moments, List<Window> windows, Run earlier, Run later, MeasuredStretch before, MeasuredStretch after, int notBefore)
    {
        // Where the later run's windows start.
        double from = windows[later.FirstWindow].StartFrame;

        // Where the earlier run's windows end.
        double until = windows[earlier.LastWindow].StartFrame + WindowFrames;
        if (until < from)
        {
            (from, until) = (until, from);
        }

        int first = notBefore;
        while (first < moments.Count && moments[first] < from)
        {
            first++;
        }

        int last = first;
        while (last < moments.Count && moments[last] < until)
        {
            last++;
        }

        if (last == first)
        {
            return Math.Clamp(first, notBefore + 1, moments.Count - 1);
        }

        int widest = first;
        double widestGap = 0;
        for (int at = Math.Max(first, notBefore + 1); at < Math.Min(last + 1, moments.Count); at++)
        {
            double gap = moments[at] - moments[at - 1];
            if (gap <= widestGap)
            {
                continue;
            }

            widestGap = gap;
            widest = at;
        }

        double longerStepAtWidestGap = Math.Max(PeriodAt(before, moments[widest]), PeriodAt(after, moments[widest]));
        if (widestGap >= PauseSteps * longerStepAtWidestGap)
        {
            return Math.Clamp(widest, notBefore + 1, moments.Count - 1);
        }

        double total = 0;
        for (int at = first; at < last; at++)
        {
            total += ClosenessToLine(after, moments[at]);
        }

        int bestAt = first;
        double best = total;
        for (int at = first; at < last; at++)
        {
            total += ClosenessToLine(before, moments[at]) - ClosenessToLine(after, moments[at]);
            if (total <= best)
            {
                continue;
            }

            best = total;
            bestAt = at + 1;
        }

        return Math.Clamp(bestAt, notBefore + 1, moments.Count - 1);
    }

    // How long a stretch's step is at a frame.
    private static double PeriodAt(MeasuredStretch stretch, double frame)
    {
        return stretch.PeriodFrames * Math.Exp(-stretch.GrowthPerFrame * (frame - stretch.OriginFrame));
    }

    // How near a moment is to one of a stretch's lines, 1 on a line and -1 halfway between two.
    private static double ClosenessToLine(MeasuredStretch stretch, double frame)
    {
        return Math.Cos(2 * Math.PI * (StepsAfter(stretch.PeriodFrames, stretch.GrowthPerFrame, frame - stretch.OriginFrame) - stretch.PhaseSteps));
    }

    // The measured stretches spelled as steps and tempos, each started on its own line and on a tick that is a whole number of its steps.
    private static SpcGrid SpellStretches(IReadOnlyList<long> moments, long endFrame, List<MeasuredStretch> measured, List<int> splits, List<int> momentCounts)
    {
        var starts = StretchStarts(moments, measured, splits);
        int longest = LongestStretchIndex(starts, endFrame);
        int songStepTicks = StepTicksNearPreferredTempo(PeriodHalfwayAlong(starts, longest, endFrame));
        int songTempo = NearestTempo(PeriodHalfwayAlong(starts, longest, endFrame), songStepTicks);
        var stretches = ImmutableArray.CreateBuilder<SpcStretch>(starts.Count);
        for (int at = 0; at < starts.Count; at++)
        {
            var (frame, period, growth) = starts[at];
            double closingPeriod = period * Math.Exp(-growth * (HandOverFrame(starts, at, endFrame) - frame));
            int stepTicks = at == longest ? songStepTicks : StepTicksBesideTempo(PeriodHalfwayAlong(starts, at, endFrame), songTempo);
            if (!IsPlayable(period, stepTicks) || !IsPlayable(closingPeriod, stepTicks))
            {
                stepTicks = StepTicksNearPreferredTempo(PeriodHalfwayAlong(starts, at, endFrame));
            }

            int startTick = at == 0 ? 0 : StartTickAfter(stretches[at - 1], frame, stepTicks, songStepTicks, moments[splits[at - 1] - 1]);
            int startTempo = NearestTempo(period, stepTicks);
            int endTempo = growth == 0 ? startTempo : NearestTempo(closingPeriod, stepTicks);
            stretches.Add(new SpcStretch(frame, startTick, period, growth, stepTicks, startTempo, endTempo));
        }

        return new SpcGrid(songStepTicks, stretches.MoveToImmutable(), WeightedFitScore(measured, momentCounts));
    }

    // Each stretch's first line, its period there and its growth, a stretch whose first line would fall at or before the one in front's taking a later line of its own.
    private static List<StretchStart> StretchStarts(IReadOnlyList<long> moments, List<MeasuredStretch> measured, List<int> splits)
    {
        var starts = new List<StretchStart>();
        for (int at = 0; at < measured.Count; at++)
        {
            var stretch = measured[at];
            long firstMoment = moments[at == 0 ? 0 : splits[at - 1]];
            double opening = FirstLineFrame(stretch, firstMoment);
            double line = opening;
            for (int later = 1; at > 0 && line <= starts[at - 1].Frame; later++)
            {
                line = opening + FramesForSteps(PeriodAt(stretch, opening), stretch.GrowthPerFrame, later);
            }

            starts.Add(new StretchStart(line, PeriodAt(stretch, line), stretch.GrowthPerFrame));
        }

        return starts;
    }

    // The index of the stretch that runs longest.
    private static int LongestStretchIndex(List<StretchStart> starts, long endFrame)
    {
        int longest = 0;
        double longestFrames = 0;
        for (int at = 0; at < starts.Count; at++)
        {
            double stretchFrames = HandOverFrame(starts, at, endFrame) - starts[at].Frame;
            if (stretchFrames <= longestFrames)
            {
                continue;
            }

            longestFrames = stretchFrames;
            longest = at;
        }

        return longest;
    }

    // The tick a stretch starts on after the one in front: on a line of its own and of the song's both, nearest where the frame falls, and past the last moment the stretch in front holds.
    private static int StartTickAfter(SpcStretch previous, double frame, int stepTicks, int songStepTicks, long lastMomentBefore)
    {
        double exactTicks = previous.StartTick + previous.StepsAt(frame) * previous.StepTicks;
        int lastMomentBeforeTick = (int)Math.Ceiling(previous.StartTick + previous.StepsAt(lastMomentBefore) * previous.StepTicks);

        // How far apart the lines of the stretch's own and of the song's both stand.
        int alignTicks = stepTicks / GreatestCommonDivisor(stepTicks, songStepTicks) * songStepTicks;

        // The nearest of those lines to where the frame falls.
        int startTick = (int)Math.Round(exactTicks / alignTicks) * alignTicks;
        while (startTick <= Math.Max(lastMomentBeforeTick, previous.StartTick))
        {
            startTick += alignTicks;
        }

        return startTick;
    }

    // How much of the song sits on its own rhythm, each stretch's score weighted by the moments it holds.
    private static double WeightedFitScore(List<MeasuredStretch> measured, List<int> momentCounts)
    {
        double fitScore = 0;
        int moments = 0;
        for (int at = 0; at < measured.Count; at++)
        {
            fitScore += measured[at].FitScore * momentCounts[at];
            moments += momentCounts[at];
        }

        return moments > 0 ? fitScore / moments : 0;
    }

    // The greatest whole number both divide by.
    private static int GreatestCommonDivisor(int one, int other)
    {
        return other == 0 ? one : GreatestCommonDivisor(other, one % other);
    }

    // Where a stretch hands over to the next, or the song's end for the last.
    private static double HandOverFrame(List<StretchStart> starts, int at, long endFrame)
    {
        return at + 1 < starts.Count ? starts[at + 1].Frame : Math.Max(endFrame, starts[at].Frame + 1);
    }

    // A stretch's step halfway along it.
    private static double PeriodHalfwayAlong(List<StretchStart> starts, int at, long endFrame)
    {
        var (frame, period, growth) = starts[at];
        return period * Math.Exp(-growth * (HandOverFrame(starts, at, endFrame) - frame) / 2);
    }

    // Where a stretch's first line falls: the last one at or before its first moment, give or take the tidying's margin.
    private static double FirstLineFrame(MeasuredStretch stretch, long firstMoment)
    {
        double firstMomentSteps = StepsAfter(stretch.PeriodFrames, stretch.GrowthPerFrame, firstMoment - stretch.OriginFrame);
        double firstLineSteps = stretch.PhaseSteps + Math.Floor(firstMomentSteps - stretch.PhaseSteps + SpcGrid.TidyWithinSteps);
        return stretch.OriginFrame + FramesForSteps(stretch.PeriodFrames, stretch.GrowthPerFrame, firstLineSteps);
    }

    // A grid for a song with no rhythm to find, which plays at the tempo a new song does.
    private static SpcGrid GridWithNoRhythm()
    {
        int stepTicks = SnesTiming.TicksPerSixteenthNote;
        int tempo = SnesTiming.DefaultTempo;
        double periodFrames = stepTicks / SnesTiming.TicksPerSecond(tempo) * DspPoint.SampleRate;
        return new SpcGrid(stepTicks, [new SpcStretch(0, 0, periodFrames, 0, stepTicks, tempo, tempo)], 0);
    }

    // How many frames a number of steps takes, the inverse of the steps after a number of frames.
    private static double FramesForSteps(double periodFrames, double growthPerFrame, double steps)
    {
        return growthPerFrame == 0 ? steps * periodFrames : double.LogP1(steps * growthPerFrame * periodFrames) / growthPerFrame;
    }

    // How well a rhythm explains the moments and where its lines fall in steps past the origin: the length and the angle of the moments' sum as points on a circle.
    private static (double FitScore, double PhaseSteps) ScoreRhythm(List<long> moments, double originFrame, double periodFrames, double growthPerFrame)
    {
        double x = 0;
        double y = 0;
        foreach (long moment in moments)
        {
            // The moment as a point on the circle, a full turn to a step.
            var (sin, cos) = Math.SinCos(2 * Math.PI * StepsAfter(periodFrames, growthPerFrame, moment - originFrame));
            x += cos;
            y += sin;
        }

        double sumAngleTurns = Math.Atan2(y, x) / (2 * Math.PI);
        if (sumAngleTurns < 0)
        {
            sumAngleTurns += 1;
        }

        return (Math.Sqrt(x * x + y * y) / moments.Count, sumAngleTurns);
    }

    // What to call one step of a period: whichever spelling puts the song nearest the preferred tempo while leaving a tempo that plays it at about its own speed, a binary one always before a triplet.
    private static int StepTicksNearPreferredTempo(double periodFrames)
    {
        double periodSeconds = periodFrames / DspPoint.SampleRate;
        int best = SnesTiming.TicksPerSixteenthNote;
        double closest = double.MaxValue;
        bool isBestTriplet = true;
        foreach (var (stepTicks, isTriplet) in Spellings)
        {
            if (!IsPlayable(periodFrames, stepTicks))
            {
                continue;
            }

            double spellingBpm = TimeUnits.SecondsPerMinute / (periodSeconds * SnesTiming.TicksPerQuarterNote / stepTicks);
            double distanceFromPreferredBpm = Math.Abs(spellingBpm - PreferredBpm);
            if (!isBestTriplet && isTriplet)
            {
                continue;
            }

            if (isBestTriplet == isTriplet && distanceFromPreferredBpm >= closest)
            {
                continue;
            }

            closest = distanceFromPreferredBpm;
            isBestTriplet = isTriplet;
            best = stepTicks;
        }

        return best;
    }

    // What to call one step of a period beside a stretch at a tempo: whichever spelling plays it at the tempo nearest that one.
    private static int StepTicksBesideTempo(double periodFrames, int tempo)
    {
        int best = StepTicksNearPreferredTempo(periodFrames);
        double closest = double.MaxValue;
        foreach (var (stepTicks, _) in Spellings)
        {
            if (!IsPlayable(periodFrames, stepTicks))
            {
                continue;
            }

            double logTempoDistance = Math.Abs(Math.Log(SnesTiming.TicksPerSecond(NearestTempo(periodFrames, stepTicks)) / SnesTiming.TicksPerSecond(tempo)));
            if (logTempoDistance >= closest)
            {
                continue;
            }

            closest = logTempoDistance;
            best = stepTicks;
        }

        return best;
    }

    // Whether some tempo plays a number of ticks in about one period.
    private static bool IsPlayable(double periodFrames, int stepTicks)
    {
        double periodSeconds = periodFrames / DspPoint.SampleRate;
        double playedSeconds = stepTicks / SnesTiming.TicksPerSecond(NearestTempo(periodFrames, stepTicks));
        return Math.Abs(playedSeconds - periodSeconds) / periodSeconds <= SpellingSlack;
    }

    // The tempo that comes nearest playing a number of ticks in one period.
    private static int NearestTempo(double periodFrames, int stepTicks)
    {
        double wantedTicksPerSecond = stepTicks / (periodFrames / DspPoint.SampleRate);
        int best = SnesTiming.DefaultTempo;
        double closest = double.MaxValue;
        for (int tempo = SnesTiming.MinTempo; tempo <= SnesTiming.MaxTempo; tempo++)
        {
            double ticksPerSecondDistance = Math.Abs(SnesTiming.TicksPerSecond(tempo) - wantedTicksPerSecond);
            if (ticksPerSecondDistance >= closest)
            {
                continue;
            }

            closest = ticksPerSecondDistance;
            best = tempo;
        }

        return best;
    }

    // One window's reading: where it starts, how many moments it held, and the period that explains them, zero where it held too few to read.
    private readonly record struct Window(double StartFrame, int MomentCount, double PeriodFrames, double FitScore)
    {
        // Whether the reading counts.
        public bool DoesCount
        {
            get
            {
                return PeriodFrames > 0 && MomentCount >= MinWindowMoments && FitScore >= MinWindowFitScore;
            }
        }

        // The middle of the window in seconds.
        public double MiddleSeconds
        {
            get
            {
                return StartFrame / DspPoint.SampleRate + WindowSeconds / 2;
            }
        }
    }

    // A run of windows read as one stretch: its first and last window, and the period it is at frame 0 and how that slides from there.
    private readonly record struct Run(int FirstWindow, int LastWindow, double PeriodFrames, double GrowthPerFrame);

    // What a run costs and the speed it is at.
    private readonly record struct RunShape(double Cost, double PeriodFrames, double GrowthPerFrame);

    // One stretch measured on its own moments: the frame its steps are counted from, the period there, how it slides, how well it fits and where its lines fall in steps past the origin.
    private readonly record struct MeasuredStretch(double OriginFrame, double PeriodFrames, double GrowthPerFrame, double FitScore, double PhaseSteps);

    // Where a stretch's first line falls, its period there and how it slides.
    private readonly record struct StretchStart(double Frame, double PeriodFrames, double GrowthPerFrame);
}
