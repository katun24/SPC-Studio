using SpcStudio.Model;

namespace SpcStudio.Audio;

// Measures a decoded sample's own frequency, by normalized autocorrelation over its loudest part with the peak interpolated between lags.
public static class BrrPitch
{
    public const double MinConfidence = 0.5;
    private const int MinPeriodPoints = 16;
    private const int MaxPeriodPoints = 1600;
    private const int WindowPoints = 4096;
    private const int WindowsInRepeatedLoop = 2;
    private const int LoudestWindowStridePoints = 64;
    private const double OctaveThreshold = 0.95;
    private const double FlatPeakCurvature = 1e-12;
    private const double UnmeasuredTuning = 1.0;
    private const int NoLag = 0;

    private static readonly double LowestPitchedHz = SnesPitch.ConcertC4Hz * SnesPitch.FrequencyRatioForSemitones(SnesPitch.MinNoteValue - SnesPitch.C4NoteValue);

    // Measures a sample's pitch, which for a looping sample is the pitch of its loop.
    public static Measurement Estimate(BrrSample sample)
    {
        return sample.DoesLoop ? Estimate(RepeatedLoop(sample)) : Estimate(sample.Points);
    }

    // Measures the pitch of a run of points at a rate.
    public static Measurement Estimate(short[] points, double sampleRate = DspPoint.SampleRate)
    {
        int window = Math.Min(WindowPoints, points.Length / 2);
        if (window < MinPeriodPoints * 2)
        {
            return default;
        }

        int maxLag = Math.Min(MaxPeriodPoints, points.Length - window - 1);
        if (maxLag < MinPeriodPoints)
        {
            return default;
        }

        int windowStart = LoudestWindowStart(points, window, points.Length - window - maxLag);
        var scores = new double[maxLag + 2];
        double peakScore = 0;
        int peakLag = NoLag;

        scores[MinPeriodPoints - 1] = Correlation(points, windowStart, window, MinPeriodPoints - 1);
        for (int lag = MinPeriodPoints; lag <= maxLag; lag++)
        {
            scores[lag] = Correlation(points, windowStart, window, lag);
            if (scores[lag] > peakScore)
            {
                peakScore = scores[lag];
                peakLag = lag;
            }
        }

        if (peakLag == NoLag)
        {
            return default;
        }

        int chosenLag = ShortestStrongPeak(scores, maxLag, peakScore * OctaveThreshold);
        if (chosenLag == NoLag)
        {
            return default;
        }

        double periodPoints = chosenLag + PeakOffset(scores, chosenLag);
        return new Measurement(sampleRate / periodPoints, periodPoints, scores[chosenLag]);
    }

    // The loop repeated until it is long enough to analyze.
    private static short[] RepeatedLoop(BrrSample sample)
    {
        int loopLength = sample.Points.Length - sample.LoopStartPoint;
        if (loopLength < 2)
        {
            return sample.Points;
        }

        int repeats = Math.Max(1, (int)Math.Ceiling(WindowPoints * (double)WindowsInRepeatedLoop / loopLength));
        var repeated = new short[loopLength * repeats];
        for (int point = 0; point < repeated.Length; point++)
        {
            repeated[point] = sample.Points[sample.LoopStartPoint + point % loopLength];
        }

        return repeated;
    }

    // The start of the loudest window of points, searched at a fixed stride.
    private static int LoudestWindowStart(short[] points, int window, int lastStart)
    {
        if (lastStart <= 0)
        {
            return 0;
        }

        var energySumBeforePoint = RunningSums.OfEnergy(points, 0, points.Length);

        int loudestStart = 0;
        double loudestEnergy = -1;
        for (int start = 0; start <= lastStart; start += LoudestWindowStridePoints)
        {
            double windowEnergy = energySumBeforePoint[start + window] - energySumBeforePoint[start];
            if (windowEnergy > loudestEnergy)
            {
                loudestEnergy = windowEnergy;
                loudestStart = start;
            }
        }

        return loudestStart;
    }

    // The correlation of the window with itself a lag later, divided by both windows' energy.
    private static double Correlation(short[] points, int windowStart, int window, int lag)
    {
        double product = 0;
        double energyEarlier = 0;
        double energyLater = 0;

        for (int point = 0; point < window; point++)
        {
            double earlier = points[windowStart + point];
            double later = points[windowStart + point + lag];
            product += earlier * later;
            energyEarlier += earlier * earlier;
            energyLater += later * later;
        }

        return NormalizedCorrelation.Of(product, energyEarlier, energyLater);
    }

    // The shortest lag that is a local maximum and scores at least the threshold, or no lag where the best score sits at an end of the searched range.
    private static int ShortestStrongPeak(double[] scores, int maxLag, double threshold)
    {
        for (int lag = MinPeriodPoints; lag < maxLag; lag++)
        {
            if (scores[lag] < threshold)
            {
                continue;
            }

            if (scores[lag] < scores[lag - 1] || scores[lag] < scores[lag + 1])
            {
                continue;
            }

            return lag;
        }

        return NoLag;
    }

    // The peak's position between lags, by fitting a parabola to it and its two neighbors.
    private static double PeakOffset(double[] scores, int lag)
    {
        double before = scores[lag - 1];
        double at = scores[lag];
        double after = scores[lag + 1];
        double curvature = before - 2 * at + after;
        if (Math.Abs(curvature) < FlatPeakCurvature)
        {
            return 0;
        }

        double offset = 0.5 * (before - after) / curvature;
        return Math.Abs(offset) <= 1 ? offset : 0;
    }

    // One measurement: the frequency, the period in points, and the peak's normalized correlation, one for a perfectly periodic signal.
    public readonly record struct Measurement(double FrequencyHz, double PeriodPoints, double Confidence)
    {
        // Whether the sample was periodic enough for the frequency to mean anything, at a pitch the roll has.
        public bool IsPitched
        {
            get
            {
                return Confidence >= MinConfidence && FrequencyHz >= LowestPitchedHz;
            }
        }

        // The tuning multiplier that plays the measured frequency at concert pitch, or one where nothing periodic was measured.
        public double TuningMultiplier
        {
            get
            {
                return IsPitched ? SnesPitch.TuningForConcertPitch(FrequencyHz) : UnmeasuredTuning;
            }
        }

        // The same sound measured in its points once converted to a fraction of the rate it was measured at: the period that fraction of the points, and the
        // frequency it sounds at the DSP's rate that much higher.
        public Measurement AtRateFraction(double rateFraction)
        {
            return this with { FrequencyHz = FrequencyHz / rateFraction, PeriodPoints = PeriodPoints * rateFraction };
        }
    }
}
