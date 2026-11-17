using System.Numerics;
using SpcStudio.Model;

namespace SpcStudio.Audio;

// A .brr imported into the song: the bytes it plays and exports and their decode, made from its source through its effects, and the pitch measurement
// given at the rate it was first made at.
public sealed record Sample(int Id, string FileName, byte[] FileBytes, BrrSample Decoded, BrrPitch.Measurement Pitch)
{
    public const int NoiseId = -1;
    public const string NoiseName = "Noise";
    public const string BrrExtension = ".brr";
    public const string WavExtension = ".wav";
    public const string DialogFilter = DialogFilters.BrrSample + DialogFilters.Separator + DialogFilters.AllFiles;
    public const int SampleTableBytes = 4;
    public const int BrrSourceRateHz = (int)DspPoint.SampleRate;
    public const double OriginalRateFraction = 1.0;
    public const double WholeRateFraction = 1.0;
    private const int MinRateHz = 1000;
    private const int MinFittedCycleCount = 1;
    private const int MinFittedBlockCount = 1;
    private const int NewSampleLoopStartPoint = 0;
    private const string ChordNameEnding = " Chord";

    public static readonly ValueRange PreviewPitchRegisterRange = new(SnesPitch.MinPitchRegister, SnesPitch.MaxPitchRegister);

    public string? PathInSampleGroup { get; init; }

    // The audio the sample's effects are applied over, at the sample's rate, with its loop: the kept source converted to that rate, which every edit
    // counts in.
    public BrrSample Source { get; init; } = Decoded;

    // The audio the Rate slider converts the source from, at the kept rate, which every edit of the source but a change of rate sets to the source.
    public BrrSample KeptSource { get; init; } = Decoded;

    // The .brr the kept source arrived as, for as long as it is that file's audio and loop, which the sample plays and exports verbatim at the kept rate
    // where no effect changes the audio, or null once the source is edited.
    public byte[]? ArrivedBrrBytes { get; init; } = FileBytes;

    // The effects made over the source into what the sample plays, as they were set at the kept rate.
    public SampleRack Effects { get; init; } = SampleRack.AllOffAt(BrrSourceRateHz);

    // The PITCH register the Sample Editor was set to play the sample at, or null where it plays the sample's own C4.
    public int? ChosenPreviewPitchRegister { get; init; }

    // Whether an edit of the audio keeps the pitch measurement rather than measuring what plays again, as a chord sample keeps its source's.
    public bool IsPitchKeptThroughEdits { get; init; }

    // The rate in hertz the sample was first made at - a .wav's own, or the DSP's own for a sample from BRR bytes - which the tuning of every instrument on
    // it and its pitch measurement are held at.
    public int OriginalRateHz { get; init; } = BrrSourceRateHz;

    // The rate in hertz the kept source's points are at, which is the highest the Rate slider goes.
    public int KeptRateHz { get; init; } = BrrSourceRateHz;

    // The rate in hertz the source's points are at: the kept rate until the Rate slider lowers it.
    public int RateHz { get; init; } = BrrSourceRateHz;

    // The PITCH register the Sample Editor plays the sample at, which nothing but its own preview reads: the one it was set to, or else the one that
    // sounds the sample's measured pitch as C4, held to the preview's range.
    public int PreviewPitchRegister
    {
        get
        {
            return ChosenPreviewPitchRegister ?? PreviewPitchRegisterRange.Clamp(SnesPitch.PitchRegisterAt(0, C4Tuning));
        }
    }

    // How much of its original rate the sample is at, which the tuning of every instrument on it is scaled by.
    public double RateFractionOfOriginal
    {
        get
        {
            return RateFractionOfOriginalAt(RateHz);
        }
    }

    // The rates the Rate slider may take: up to the kept rate, and down to the lowest rate offered or to the kept rate where that is lower.
    public ValueRange RateRange
    {
        get
        {
            return new(Math.Min(MinRateHz, KeptRateHz), KeptRateHz);
        }
    }

    // The effects as they apply at the sample's rate: every setting the rate cannot hold moved down to its top.
    public SampleRack EffectsAtRate
    {
        get
        {
            return Effects.HeldTo(RateHz);
        }
    }

    // The tuning that plays the sample's measured pitch as C4 at its original rate, which is the tuning an instrument on it holds.
    public double OriginalRateC4Tuning
    {
        get
        {
            return Pitch.TuningMultiplier;
        }
    }

    // The sample's pitch measurement given at the rate it is at, which is what its own points are counted in.
    public BrrPitch.Measurement PitchAtRate
    {
        get
        {
            return PitchAt(RateHz);
        }
    }

    // The tuning that plays the sample's measured pitch as C4 at its rate.
    public double C4Tuning
    {
        get
        {
            return OriginalRateC4Tuning * RateFractionOfOriginal;
        }
    }

    // The sample's file name with its extension taken off and any folder in front of it kept, which is the name the app shows and renames.
    public string Name
    {
        get
        {
            return NameOf(FileName);
        }
    }

    // The sample's file name with its extension and any folder in front of it taken off, which is what an instrument built on it is named after.
    public string NameStem
    {
        get
        {
            return Path.GetFileNameWithoutExtension(FileName);
        }
    }

    // How many BRR blocks the sample plays, which is never fewer than one.
    public int BlockCount
    {
        get
        {
            return Decoded.Points.Length / BrrSample.PointsPerBlock;
        }
    }

    // How many audio points the sample holds.
    public int PointCount
    {
        get
        {
            return Decoded.Points.Length;
        }
    }

    // What the sample costs a song in ARAM: its blocks and the DSP sample table entry that points at them.
    public int AramBytes
    {
        get
        {
            return AramBytesOf(BlockCount);
        }
    }

    // Whether the source is only the one silent block a sample is left with when every point is taken out.
    public bool IsEmpty
    {
        get
        {
            var points = Source.Points;
            return points.Length == BrrSample.PointsPerBlock && DspPoint.LeadingSilentCount(points) == points.Length;
        }
    }

    // Reads a .brr or a .wav and measures it, the song assigning its id.
    public static Sample Load(string path, Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        string fileName = Path.GetFileName(path);
        byte[] fileBytes = File.ReadAllBytes(path);

        return FileExtension.Matches(path, WavExtension)
            ? FromWav(fileName, fileBytes, progress, cancel)
            : FromBytes(fileName, fileBytes);
    }

    // A sample from .brr bytes already in hand, measured on the way in exactly as a loaded one is, refusing bytes that are not BRR as not a valid BRR sample.
    public static Sample FromBytes(string fileName, byte[] fileBytes)
    {
        BrrSample decoded;
        try
        {
            decoded = BrrSample.Decode(fileBytes, fileName);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException($"{SavedFileJson.QuotedFileName(fileName)} is not a valid BRR sample.");
        }

        return new Sample(0, fileName, fileBytes, decoded, BrrPitch.Estimate(decoded));
    }

    // A sample from the bytes of a .wav: the audio read out of it as its source, under the .brr name at the file's own rate, which it keeps as its original
    // rate.
    public static Sample FromWav(string fileName, byte[] fileBytes, Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        var points = WavReader.ReadMonoPoints(fileBytes, fileName);
        var sample = NewFromPoints(Path.ChangeExtension(fileName, BrrExtension), points, progress, cancel);
        int fileRateHz = WavReader.ReadRateHz(fileBytes, fileName);
        return sample with { OriginalRateHz = fileRateHz, KeptRateHz = fileRateHz, RateHz = fileRateHz, Effects = SampleRack.AllOffAt(fileRateHz) };
    }

    // A new empty sample of one silent block, under a name the caller has already made sure is free.
    public static Sample Empty(string fileName)
    {
        return NewFromPoints(fileName, []);
    }

    // A new sample over points as its source, looping from its start, encoded once and measured over what it plays as a one-shot.
    private static Sample NewFromPoints(string fileName, short[] points, Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        var source = BrrSample.FromPoints(points, NewSampleLoopStartPoint, doesLoop: true);
        byte[] fileBytes = BrrEncoder.Encode(source, progress, cancel);
        var decoded = BrrSample.Decode(fileBytes, fileName);
        return new Sample(0, fileName, fileBytes, decoded, BrrPitch.Estimate(decoded.Points)) { Source = source, KeptSource = source, ArrivedBrrBytes = null };
    }

    // A sample from a saved file at its kept rate: what it plays made again from the stored kept source, the .brr it arrived as where the file still holds
    // it, and the effects, and the two rates and the measurement taken as read.
    public static Sample FromSaved(int id, string fileName, BrrSample keptSource, byte[]? arrivedBrrBytes, SampleRack effects, BrrPitch.Measurement pitch,
        int originalRateHz, int keptRateHz)
    {
        var saved = new Sample(id, fileName, [], keptSource, pitch) { OriginalRateHz = originalRateHz, KeptRateHz = keptRateHz, RateHz = keptRateHz };
        return saved.Remade(keptSource, arrivedBrrBytes, effects);
    }

    // The same sample set to play in the Sample Editor at a PITCH register held to the preview's range, or this one where it is set to that already.
    public Sample WithPreviewPitchRegister(int pitchRegister)
    {
        int heldPitchRegister = PreviewPitchRegisterRange.Clamp(pitchRegister);
        return heldPitchRegister == ChosenPreviewPitchRegister ? this : this with { ChosenPreviewPitchRegister = heldPitchRegister };
    }

    // The same sample playing in the Sample Editor at its own C4 again, or this one where it does already.
    public Sample WithPreviewOnOwnC4()
    {
        return ChosenPreviewPitchRegister is null ? this : this with { ChosenPreviewPitchRegister = null };
    }

    // The same sample with its source converted from the kept source to a rate held to its range, the kept source kept, the loop start put on the block
    // nearest where it was and the effects applied over the result at that rate, or this one where it is at that rate already.
    public Sample ConvertedTo(int rateHz)
    {
        int heldRateHz = RateRange.Clamp(rateHz);
        if (heldRateHz == RateHz)
        {
            return this;
        }

        double conversionFraction = heldRateHz / (double)KeptRateHz;
        int loopPoint = NearestBlockStart((int)Math.Round(KeptSource.LoopStartPoint * conversionFraction));
        var source = heldRateHz == KeptRateHz ? KeptSource
            : BrrSample.FromPoints(PointResampler.Resampled(KeptSource.Points, conversionFraction), loopPoint, KeptSource.DoesLoop);
        return WithRateSet(heldRateHz).Remade(source, ArrivedBrrBytes, Effects);
    }

    // What the sample would cost a song in ARAM converted to a rate held to its range, worked out from its length at that rate without converting it.
    public int AramBytesAt(int rateHz)
    {
        double conversionFraction = RateRange.Clamp(rateHz) / (double)KeptRateHz;
        return AramBytesOf(BrrSample.BlockCountFor(PointResampler.ResampledCount(KeptSource.Points.Length, conversionFraction)));
    }

    // What a sample of a number of blocks costs a song in ARAM: its blocks and the DSP sample table entry that points at them.
    public static T AramBytesOf<T>(T blockCount) where T : IBinaryInteger<T>
    {
        return blockCount * T.CreateChecked(BrrSample.BlockSize) + T.CreateChecked(SampleTableBytes);
    }

    // The same sample set to another rate, the measurement carried over and a preview register that was set scaled by the change so the Sample Editor's
    // preview keeps sounding the same pitch.
    private Sample WithRateSet(int rateHz)
    {
        double rateChange = RateChangeTo(rateHz);
        int? previewPitchRegister = ChosenPreviewPitchRegister is int chosen ? PreviewPitchRegisterRange.Clamp((int)Math.Round(chosen * rateChange)) : null;
        return this with { RateHz = rateHz, ChosenPreviewPitchRegister = previewPitchRegister };
    }

    // Whether a loop can be fitted to whole blocks by lowering the sample's rate by no more than a fraction of it: a pitched sound, a loop of at least
    // one of its cycles and one block, and a rate the sample may be converted to.
    public bool CanFitLoop(LoopFit fit, double maxRateLossFraction)
    {
        var pitch = PitchAtRate;
        if (!pitch.IsPitched || pitch.CycleCountIn(fit.RepeatPoints) < MinFittedCycleCount || fit.BlockCount < MinFittedBlockCount)
        {
            return false;
        }

        return WholeRateFraction - fit.RateFraction <= maxRateLossFraction && FittedRateHz(fit) >= RateRange.Min;
    }

    // The sample's source converted to the rate that turns a loop's repeat length into whole blocks, not yet encoded: its points up to the loop's end, the
    // loop's start, which silence at the sample's start puts on a block, and the rate.
    public FittedLoop FittedLoopOf(LoopFit fit)
    {
        double rateFraction = fit.RateFraction;
        int pointsBeforeLoop = (int)Math.Floor(fit.StartPoint * rateFraction);
        var points = rateFraction == WholeRateFraction ? PointsInRange(0, fit.StartPoint + fit.FittedPointCount)
            : PointResampler.ResampledFrom(SourcePointsReadOnIntoLoop(rateFraction), rateFraction, fit.StartPoint - pointsBeforeLoop / rateFraction,
                pointsBeforeLoop + fit.FittedPointCount);
        var (alignedPoints, loopStartPoint) = WithLoopPointOnBlockStart(points, pointsBeforeLoop);
        return new FittedLoop(alignedPoints, loopStartPoint, FittedRateHz(fit));
    }

    // The same sample converted to the rate that fits a loop to whole blocks, everything after the loop cut off and looping from its start, with the
    // measurement carried over.
    public Sample WithLoopFitted(LoopFit fit)
    {
        var fitted = FittedLoopOf(fit);
        return WithFittedSource(fitted, fitted.Points);
    }

    // The same sample converted to the rate that fits a loop to whole blocks, its points up to the loop's end replaced by others at that rate, looping
    // from the fitted loop's start and encoded once, with the measurement carried over.
    public Sample WithLoopFittedFrom(LoopFit fit, short[] pointsToLoopEnd)
    {
        return WithFittedSource(FittedLoopOf(fit), pointsToLoopEnd);
    }

    // The same sample at a fitted loop's rate over points that loop from its start, encoded once.
    private Sample WithFittedSource(FittedLoop fitted, short[] points)
    {
        return WithRateSet(fitted.RateHz).WithSource(SourceOf(points, fitted.LoopStartPoint, doesLoop: true));
    }

    // The sample's pitch measurement given at a rate, which is what points at that rate are counted in.
    public BrrPitch.Measurement PitchAt(int rateHz)
    {
        return Pitch.AtRateFraction(RateFractionOfOriginalAt(rateHz));
    }

    // How many times the sample's rate a rate is.
    public double RateChangeTo(int rateHz)
    {
        return rateHz / (double)RateHz;
    }

    // How many times one sample's rate another's is, where the two are one sample before and after an edit, or no change where they are not.
    public static double RateChangeBetween(Sample? before, Sample? after)
    {
        if (before is null || after is null || before.Id != after.Id)
        {
            return WholeRateFraction;
        }

        return before.RateChangeTo(after.RateHz);
    }

    // The point at the same moment as a point of a sample once its rate has changed by a number of times.
    public static int PointAfterRateChange(int point, double rateChange)
    {
        return (int)Math.Round(point * rateChange);
    }

    // How much of the sample's original rate a rate is.
    private double RateFractionOfOriginalAt(int rateHz)
    {
        return rateHz / (double)OriginalRateHz;
    }

    // The rate in hertz that fits a loop to whole blocks, to the nearest hertz.
    private int FittedRateHz(LoopFit fit)
    {
        return (int)Math.Round(RateHz * fit.RateFraction);
    }

    // The source's points, read on past the last into the loop's start where the sample loops - what plays after them - as far as a conversion to a
    // fraction of the rate reads past a point.
    private short[] SourcePointsReadOnIntoLoop(double rateFraction)
    {
        return Source.PointsReadOnIntoLoopFor(PointResampler.ReachInOldPoints(rateFraction));
    }

    // The same sample with its loop flag set or cleared, or this one where it already says what is asked for.
    public Sample WithLoopFlag(bool doesLoop)
    {
        if (Source.DoesLoop == doesLoop)
        {
            return this;
        }

        return WithSource(BrrSample.FromPoints(Source.Points, Source.LoopStartPoint, doesLoop));
    }

    // The same sample with its loop starting at a block, clamped into the sample, or this one where it starts there already.
    public Sample WithLoopBlock(int block)
    {
        var movedSource = BrrSample.FromPoints(Source.Points, block * BrrSample.PointsPerBlock, Source.DoesLoop);
        if (movedSource.LoopStartPoint == Source.LoopStartPoint)
        {
            return this;
        }

        return WithSource(movedSource);
    }

    // The source's audio over a range of points, clamped into the sample - what the sample editor's clipboard holds.
    public short[] PointsInRange(int start, int count)
    {
        (start, count) = RangeClampedIntoPoints(start, count);
        return Source.Points.AsSpan(start, count).ToArray();
    }

    // The source's audio over a range of points, clamped into the sample, with the rate it is at - what the sample editor's clipboard holds.
    public CopiedPoints CopiedPointsInRange(int start, int count)
    {
        return new CopiedPoints(PointsInRange(start, count), RateHz);
    }

    // The same sample with points added onto the source's from a point clamped into it, the whole sum turned down evenly where it would not fit; a loop
    // the added points reach into starts again at the first block after them, keeping its length, the source played on through its loop to get there.
    public Sample WithPointsMixedIn(int start, short[] addedPoints)
    {
        (start, _) = RangeClampedIntoPoints(start, 0);
        int addedEndPoint = start + addedPoints.Length;
        var sourcePoints = Source.Points;
        int loopStartPoint = Source.LoopStartPoint;
        if (Source.DoesLoop && addedEndPoint > loopStartPoint)
        {
            int movedLoopStartPoint = (int)BrrSample.BlockStartAtOrAfter(addedEndPoint);
            sourcePoints = Source.PointsReadOnIntoLoopFor(movedLoopStartPoint - loopStartPoint);
            loopStartPoint = movedLoopStartPoint;
        }

        short[] addedFromSampleStart = [.. new short[start], .. addedPoints];
        return WithSourcePoints(PointMix.Summed([sourcePoints, addedFromSampleStart]), loopStartPoint, Source.DoesLoop);
    }

    // The same sample with a run of the source's points taken out and others put in their place, re-encoded whole with the loop point following the audio it named.
    public Sample WithPointsReplaced(int start, int count, short[] replacement)
    {
        var points = Source.Points;
        (start, count) = RangeClampedIntoPoints(start, count);

        var splicedPoints = new short[points.Length - count + replacement.Length];
        points.AsSpan(0, start).CopyTo(splicedPoints);
        replacement.CopyTo(splicedPoints.AsSpan(start));
        points.AsSpan(start + count).CopyTo(splicedPoints.AsSpan(start + replacement.Length));

        return WithSourcePoints(splicedPoints, LoopPointAfterSplice(Source.LoopStartPoint, start, count, replacement.Length), Source.DoesLoop);
    }

    // The run of the source's points between a start and an end, clamped into the sample, cut down to the part from its first sounding point to its last -
    // an empty run at its start where every point in it is silent.
    public (int Start, int End) SoundingRunWithin(int start, int end)
    {
        (start, int count) = RangeClampedIntoPoints(start, end - start);
        var run = Source.Points.AsSpan(start, count);
        int leadingSilentPoints = DspPoint.LeadingSilentCount(run);
        if (leadingSilentPoints == count)
        {
            return (start, start);
        }

        return (start + leadingSilentPoints, start + count - DspPoint.TrailingSilentCount(run));
    }

    // The same sample with the source's points between a start and an end taken out but for a run inside them, re-encoded whole with the loop point
    // following the audio it named, or held at the nearer end of the run where that audio is gone.
    public Sample WithOnlyRunKeptWithin(int start, int end, int keptStart, int keptEnd)
    {
        (start, int count) = RangeClampedIntoPoints(start, end - start);
        end = start + count;
        keptStart = Math.Clamp(keptStart, start, end);
        keptEnd = Math.Clamp(keptEnd, keptStart, end);

        var points = Source.Points;
        short[] keptPoints = [.. points[..start], .. points[keptStart..keptEnd], .. points[end..]];
        int loopPointAfterEndCut = LoopPointAfterSplice(Source.LoopStartPoint, keptEnd, end - keptEnd, 0);
        return WithSourcePoints(keptPoints, LoopPointAfterSplice(loopPointAfterEndCut, start, keptStart - start, 0), Source.DoesLoop);
    }

    // The same sample with a run of the source's points written over by as many others, re-encoded whole with the loop point where it was.
    public Sample WithPointsOverwritten(int start, short[] replacement)
    {
        var points = (short[])Source.Points.Clone();
        (start, int count) = RangeClampedIntoPoints(start, replacement.Length);
        replacement.AsSpan(0, count).CopyTo(points.AsSpan(start));

        return WithSourcePoints(points, Source.LoopStartPoint, Source.DoesLoop);
    }

    // The same sample with its points up to a loop's end replaced by others, cut off there and looping from the loop's start, with the measurement carried
    // over.
    public Sample WithLoopFrom(int loopStartPoint, short[] pointsToLoopEnd)
    {
        return WithSource(SourceOf(pointsToLoopEnd, loopStartPoint, doesLoop: true));
    }

    // The sample's points from its start to a loop's end, with silence at their start putting the loop's start on a block, and where the loop now starts.
    public (short[] Points, int LoopPoint) LoopOnBlockStartOf(int loopStartPoint, int loopEndPoint)
    {
        return WithLoopPointOnBlockStart(PointsInRange(0, loopEndPoint), loopStartPoint);
    }

    // A sample file name with its extension taken off and any folder in front of it kept: the name the app shows it under.
    public static string NameOf(string fileName)
    {
        return fileName[..^Path.GetExtension(fileName).Length];
    }

    // Whether a name is the one no sample may take: the noise generator's, which the sample drop-down offers beside the samples.
    public static bool IsReservedName(string name)
    {
        return name == NoiseName;
    }

    // Whether another sample holds the same kept source, arrived as the same .brr or as none, under the same effects at the same rates, and so plays the
    // same audio.
    public bool PlaysSameAudioAs(Sample other)
    {
        return KeptSource.IsSameAs(other.KeptSource) && ArrivedBrrBytes.AsSpan().SequenceEqual(other.ArrivedBrrBytes) && Effects.IsSameAs(other.Effects)
            && OriginalRateHz == other.OriginalRateHz && KeptRateHz == other.KeptRateHz && RateHz == other.RateHz;
    }

    // The same sample under another name, its extension kept, or this one where it is called that already.
    public Sample WithName(string name)
    {
        string renamedFileName = name + Path.GetExtension(FileName);
        return renamedFileName == FileName ? this : this with { FileName = renamedFileName };
    }

    // The same sample over new source points, the loop starting at the block nearest a point and the loop flag as given, and the result measured again.
    private Sample WithSourcePoints(short[] points, int loopPoint, bool doesLoop)
    {
        return Remeasured(WithSource(SourceOf(points, loopPoint, doesLoop)));
    }

    // New source points as the source holds them, looping from the block nearest a point - a loop's start put on a block by silence at their start, so its
    // audio is kept whole - and looping or not as given.
    private static BrrSample SourceOf(short[] points, int loopPoint, bool doesLoop)
    {
        if (!doesLoop)
        {
            return BrrSample.FromPoints(points, NearestBlockStart(loopPoint), doesLoop);
        }

        var (alignedPoints, alignedLoopPoint) = WithLoopPointOnBlockStart(points, loopPoint);
        return BrrSample.FromPoints(alignedPoints, alignedLoopPoint, doesLoop);
    }

    // Points whose loop point is moved onto a block start by silence at their start: silent points already there taken away where there are enough, and
    // the rest of a block's worth added where there are not, so the audio from the loop point on is kept whole.
    private static (short[] Points, int LoopPoint) WithLoopPointOnBlockStart(short[] points, int loopPoint)
    {
        int pointsPastBlockStart = loopPoint % BrrSample.PointsPerBlock;
        if (pointsPastBlockStart == 0)
        {
            return (points, loopPoint);
        }

        int leadingSilentPoints = DspPoint.LeadingSilentCount(points.AsSpan(0, loopPoint));
        if (leadingSilentPoints >= pointsPastBlockStart)
        {
            return (points[pointsPastBlockStart..], loopPoint - pointsPastBlockStart);
        }

        int addedSilentPoints = BrrSample.PointsPerBlock - pointsPastBlockStart;
        var padded = new short[addedSilentPoints + points.Length];
        points.CopyTo(padded, addedSilentPoints);
        return (padded, loopPoint + addedSilentPoints);
    }

    // The same sample under different effects as they apply at its rate - a value that is a setting held to the rate keeping the setting - applied over its
    // source again and the result measured again, or this one where it has those effects already.
    public Sample WithEffects(SampleRack effectsAtRate)
    {
        var effects = effectsAtRate.WithHeldValuesRestored(Effects, RateHz);
        return effects.IsSameAs(Effects) ? this : Remeasured(Remade(Source, ArrivedBrrBytes, effects));
    }

    // A new sample playing a chord made over this one's source, named after this one with the chord's ending, at this one's rate under its effects, its
    // preview and its measurement, the measurement kept through every later edit.
    public Sample ChordSampleOf(BrrSample chordSource, CancellationToken cancel = default)
    {
        string chordFileName = Name + ChordNameEnding + Path.GetExtension(FileName);
        return (this with { FileName = chordFileName, IsPitchKeptThroughEdits = true }).WithSource(chordSource, cancel);
    }

    // The same sample over an edited source, which becomes the kept source at the sample's rate and is no longer the .brr it arrived as, its effects held
    // for good to what that rate can hold and made over it again, and the measurement carried over.
    private Sample WithSource(BrrSample source, CancellationToken cancel = default)
    {
        return (this with { KeptSource = source, KeptRateHz = RateHz }).Remade(source, null, EffectsAtRate, cancel);
    }

    // The same sample over a source, the .brr its kept source arrived as or none, and effects, what it plays made again from them and the measurement
    // carried over: the arrived .brr verbatim at the kept rate where nothing changes the audio, or else the source's points through the effects as they
    // apply at its rate, encoded under its loop.
    private Sample Remade(BrrSample source, byte[]? arrivedBrrBytes, SampleRack effects, CancellationToken cancel = default)
    {
        var withSourceAndEffects = this with { Source = source, ArrivedBrrBytes = arrivedBrrBytes, Effects = effects };
        var effectsAtRate = withSourceAndEffects.EffectsAtRate;
        bool doesChangeAudio = effectsAtRate.DoesChangeAudioAt(RateHz);
        if (!doesChangeAudio && arrivedBrrBytes is not null && RateHz == KeptRateHz)
        {
            return withSourceAndEffects with { FileBytes = arrivedBrrBytes, Decoded = source };
        }

        var playedAudio = doesChangeAudio ? BrrSample.FromPoints(effectsAtRate.Apply(source.Points, RateHz), source.LoopStartPoint, source.DoesLoop) : source;
        byte[] fileBytes = BrrEncoder.Encode(playedAudio, cancel: cancel);
        return withSourceAndEffects with { FileBytes = fileBytes, Decoded = BrrSample.Decode(fileBytes, FileName) };
    }

    // The pitch of what the sample plays, measured again and given at its original rate.
    public BrrPitch.Measurement MeasuredPitch()
    {
        return BrrPitch.Estimate(Decoded).AtRateFraction(OriginalRateFraction / RateFractionOfOriginal);
    }

    // A sample with the pitch measured again from what it plays, or the same sample where it keeps its measurement through edits.
    private static Sample Remeasured(Sample sample)
    {
        return sample.IsPitchKeptThroughEdits ? sample : sample with { Pitch = sample.MeasuredPitch() };
    }

    // A range of points held inside the sample: its start within the points, and its count within what is left after that start.
    private (int Start, int Count) RangeClampedIntoPoints(int start, int count)
    {
        int pointCount = PointCount;
        int clampedStart = Math.Clamp(start, 0, pointCount);
        return (clampedStart, Math.Clamp(count, 0, pointCount - clampedStart));
    }

    // Where a loop point lands once a splice has removed some points and inserted others: moved by the difference past the splice, and to its start inside the points removed.
    private static int LoopPointAfterSplice(int loopPoint, int start, int removedCount, int insertedCount)
    {
        if (loopPoint >= start + removedCount)
        {
            return loopPoint + insertedCount - removedCount;
        }

        return loopPoint > start ? start : loopPoint;
    }

    // The first point of the block nearest a point.
    private static int NearestBlockStart(int point)
    {
        return (int)Math.Round(point / (double)BrrSample.PointsPerBlock) * BrrSample.PointsPerBlock;
    }
}
