using SpcStudio.Model;

namespace SpcStudio.Audio;

// A chord made of one sample's audio: the audio read at the pitch of each note some semitones above or below its own, the notes summed through PointMix,
// and a loop every note fits into a whole number of times - exactly where the notes are tuned by whole-number ratios, and nearly in piano tuning, whose
// join is then crossfaded.
public static class ChordSample
{
    private const double UnisonRatio = 1.0;
    private const int FirstPoint = 0;

    // The frequency ratio of each semitone in an octave above the root under ratio tuning, the octaves above and below it doubling and halving it.
    private static readonly (long Numerator, long Denominator)[] RatiosInOctave =
        [(1, 1), (16, 15), (9, 8), (6, 5), (5, 4), (4, 3), (45, 32), (3, 2), (8, 5), (5, 3), (9, 5), (15, 8)];

    // The chord of one note or more over a source, each note given in semitones from the source's own pitch, tuned by ratios or in piano tuning, the
    // piano tuning's loop crossfaded over a fraction of its length.
    public static BrrSample AudioOf(BrrSample source, IReadOnlyCollection<int> semitones, bool isTunedByRatios, double crossfadeLengthFraction)
    {
        var layout = LayoutOf(source, semitones, isTunedByRatios, crossfadeLengthFraction);
        int pointCount = checked((int)layout.PointCount);
        int loopStartPoint = (int)layout.LoopStartPoint;
        var mixed = PointMix.Summed(semitones.Select(semitone => NotePoints(source, FrequencyRatioOf(semitone, isTunedByRatios), pointCount)));
        if (source.DoesLoop && !isTunedByRatios)
        {
            mixed = WithLoopCrossfaded(mixed, loopStartPoint, crossfadeLengthFraction);
        }

        return BrrSample.FromPoints(mixed, loopStartPoint, source.DoesLoop);
    }

    // What the chord of one note or more would cost a song in ARAM, worked out from its length without making it.
    public static long AramBytesOf(BrrSample source, IReadOnlyCollection<int> semitones, bool isTunedByRatios, double crossfadeLengthFraction)
    {
        return Sample.AramBytesOf(BrrSample.BlockCountFor(LayoutOf(source, semitones, isTunedByRatios, crossfadeLengthFraction).PointCount));
    }

    // How long the chord is and where its loop starts. A source that does not loop gives a chord as long as its lowest note. A looping one gives a loop as
    // long as the source's times every note's ratio's denominator, which every note fits a whole number of times under ratio tuning, starting at the first
    // block where every note is inside its own loop - and, in piano tuning, where the crossfade's length stands before it.
    private static ChordLayout LayoutOf(BrrSample source, IReadOnlyCollection<int> semitones, bool isTunedByRatios, double crossfadeLengthFraction)
    {
        var frequencyRatios = semitones.Select(semitone => FrequencyRatioOf(semitone, isTunedByRatios)).ToList();
        if (!source.DoesLoop)
        {
            long lowestNoteCount = frequencyRatios.Max(frequencyRatio => PointResampler.ResampledCount(source.Points.Length, UnisonRatio / frequencyRatio));
            return new ChordLayout(lowestNoteCount, FirstPoint);
        }

        long sourceLoopCount = source.Points.Length - source.LoopStartPoint;
        long denominatorsMultiple = semitones.Aggregate(1L, (multiple, semitone) => WholeNumbers.LeastCommonMultiple(multiple, RatioOf(semitone).Denominator));
        long loopCount = sourceLoopCount * denominatorsMultiple;
        double pointsBeforeLoop = frequencyRatios.Max(frequencyRatio => source.LoopStartPoint / frequencyRatio);
        if (!isTunedByRatios)
        {
            pointsBeforeLoop = Math.Max(pointsBeforeLoop, LoopCrossfade.BlendedCountOf(loopCount, crossfadeLengthFraction));
        }

        long loopStartPoint = BrrSample.BlockStartAtOrAfter(pointsBeforeLoop);
        return new ChordLayout(loopStartPoint + loopCount, loopStartPoint);
    }

    // The frequency ratio a note some semitones from the root sounds at: the ratio tuning's whole-number ratio, or the piano tuning's.
    private static double FrequencyRatioOf(int semitone, bool isTunedByRatios)
    {
        if (!isTunedByRatios)
        {
            return SnesPitch.FrequencyRatioForSemitones(semitone);
        }

        var (numerator, denominator) = RatioOf(semitone);
        return numerator / (double)denominator;
    }

    // The whole-number ratio a note some semitones from the root sounds at under ratio tuning, at its lowest terms: its octave's ratio from the table,
    // doubled for each octave above the root and halved for each below it.
    private static (long Numerator, long Denominator) RatioOf(int semitone)
    {
        int octave = (int)Math.Floor(semitone / (double)SnesPitch.SemitonesPerOctave);
        var (numerator, denominator) = RatiosInOctave[semitone - octave * SnesPitch.SemitonesPerOctave];
        if (octave >= 0)
        {
            numerator <<= octave;
        }
        else
        {
            denominator <<= -octave;
        }

        long divisor = WholeNumbers.GreatestCommonDivisor(numerator, denominator);
        return (numerator / divisor, denominator / divisor);
    }

    // One note of the chord, a count of points long: the source read at a frequency ratio from its first point, its loop played on after it where it
    // loops and silence after it where it does not - the points themselves at the root, which nothing needs to read between.
    private static short[] NotePoints(BrrSample source, double frequencyRatio, int count)
    {
        bool isUnison = frequencyRatio == UnisonRatio;
        double rateFraction = UnisonRatio / frequencyRatio;
        int reach = isUnison ? 0 : PointResampler.ReachInOldPoints(rateFraction);
        int readCount = (int)Math.Ceiling(count * frequencyRatio) + reach;
        var readOn = source.PointsReadOnIntoLoopFor(Math.Max(0, readCount - source.Points.Length));
        if (!isUnison)
        {
            return PointResampler.ResampledFrom(readOn, rateFraction, FirstPoint, count);
        }

        var unison = new short[count];
        readOn.AsSpan(FirstPoint, Math.Min(count, readOn.Length)).CopyTo(unison);
        return unison;
    }

    // Points whose loop's end is blended into the points before the loop over a fraction of its length, as Crossfade Loop blends it, or the points as
    // they are where nothing stands before the loop.
    private static short[] WithLoopCrossfaded(short[] points, int loopStartPoint, double crossfadeLengthFraction)
    {
        if (!LoopCrossfade.CanCrossfade(loopStartPoint))
        {
            return points;
        }

        return [.. points[..loopStartPoint], .. LoopCrossfade.Crossfaded(points[loopStartPoint..], points[..loopStartPoint], crossfadeLengthFraction)];
    }

    // How many points a chord holds and the point its loop starts at.
    private readonly record struct ChordLayout(long PointCount, long LoopStartPoint);
}
