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
    private const int MinRateHz = 1000;

    public static readonly ValueRange PreviewPitchRegisterRange = new(SnesPitch.MinPitchRegister, SnesPitch.MaxPitchRegister);

    // The .brr the sample's effects are applied over, at the sample's rate, which cuts, pastes, loop edits and rate conversions change.
    public byte[] SourceBytes { get; init; } = FileBytes;

    // The effects made over the source into what the sample plays.
    public SampleRack Effects { get; init; } = SampleRack.AllOffAt(BrrSourceRateHz);

    // The PITCH register the Sample Editor was set to play the sample at, or null where it plays the sample's own C4.
    public int? ChosenPreviewPitchRegister { get; init; }

    // The rate in hertz the sample was first made at - a .wav's own, or the DSP's own for a sample from BRR bytes - which the tuning of every instrument on
    // it and its pitch measurement are held at.
    public int OriginalRateHz { get; init; } = BrrSourceRateHz;

    // The rate in hertz the source's points are at, which is the original rate until the sample is converted.
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
            return RateHz / (double)OriginalRateHz;
        }
    }

    // The rates the sample may be converted to: up to its own, and down to the lowest rate offered or to its own where that is lower.
    public ValueRange ConversionRateRange
    {
        get
        {
            return new(Math.Min(MinRateHz, RateHz), RateHz);
        }
    }

    // Whether the sample has a lower rate it may be converted to.
    public bool CanConvertRate
    {
        get
        {
            return ConversionRateRange.Min < RateHz;
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
            return Pitch.AtRateFraction(RateFractionOfOriginal);
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
            return BlockCount * BrrSample.BlockSize + SampleTableBytes;
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

    // A sample from the bytes of a .wav: the audio read out of it, encoded as a one-shot under the .brr name at the file's own rate, which it keeps as its
    // original rate, and measured.
    public static Sample FromWav(string fileName, byte[] fileBytes, Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        var points = WavReader.ReadMonoPoints(fileBytes, fileName);
        var sample = FromBytes(Path.ChangeExtension(fileName, BrrExtension), BrrEncoder.Encode(points, 0, false, progress, cancel));
        int fileRateHz = WavReader.ReadRateHz(fileBytes, fileName);
        return sample with { OriginalRateHz = fileRateHz, RateHz = fileRateHz, Effects = SampleRack.AllOffAt(fileRateHz) };
    }

    // A new empty sample of one silent block, under a name the caller has already made sure is free.
    public static Sample Empty(string fileName)
    {
        return FromBytes(fileName, BrrSample.SilentFileBytes());
    }

    // A sample from a saved file: what it plays made again from the stored source and effects, and the two rates and the measurement taken as read.
    public static Sample FromSaved(int id, string fileName, byte[] sourceBytes, SampleRack effects, BrrPitch.Measurement pitch, int originalRateHz, int rateHz)
    {
        var saved = new Sample(id, fileName, sourceBytes, BrrSample.Decode(sourceBytes, fileName), pitch) { OriginalRateHz = originalRateHz, RateHz = rateHz };
        return saved.Remade(sourceBytes, effects);
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

    // The same sample with its source converted for good to a rate held to its range, the loop start put on the block nearest where it was, the
    // effects' settings held to what the rate can hold and applied over the result again, or this one where it is at that rate already.
    public Sample ConvertedTo(int rateHz)
    {
        int heldRateHz = ConversionRateRange.Clamp(rateHz);
        if (heldRateHz == RateHz)
        {
            return this;
        }

        var source = BrrSample.Decode(SourceBytes, FileName);
        double conversionFraction = heldRateHz / (double)RateHz;
        int loopPoint = NearestBlockStart((int)Math.Round(source.LoopStartPoint * conversionFraction));
        byte[] convertedBytes = BrrEncoder.Encode(PointResampler.Resampled(source.Points, conversionFraction), loopPoint, source.DoesLoop);
        return (this with { RateHz = heldRateHz }).Remade(convertedBytes, Effects.HeldTo(heldRateHz));
    }

    // The same sample with its loop flag set or cleared, or this one where it already says what is asked for.
    public Sample WithLoopFlag(bool doesLoop)
    {
        if (Decoded.DoesLoop == doesLoop)
        {
            return this;
        }

        return Remade(BrrSample.WithLoopFlag(SourceBytes, doesLoop, FileName), Effects);
    }

    // The same sample with its loop starting at a block, clamped into the sample, or this one where it starts there already.
    public Sample WithLoopBlock(int block)
    {
        byte[] sourceBytes = BrrSample.WithLoopBlock(SourceBytes, block, FileName);
        var source = BrrSample.Decode(sourceBytes, FileName);

        if (source.LoopStartPoint == Decoded.LoopStartPoint && sourceBytes.Length == SourceBytes.Length)
        {
            return this;
        }

        return Remade(sourceBytes, Effects);
    }

    // The source's audio over a range of points, clamped into the sample - what the sample editor's clipboard holds.
    public short[] PointsInRange(int start, int count)
    {
        (start, count) = RangeClampedIntoPoints(start, count);
        return SourcePoints().AsSpan(start, count).ToArray();
    }

    // The same sample with a run of the source's points taken out and others put in their place, re-encoded whole with the loop point following the audio it named.
    public Sample WithPointsReplaced(int start, int count, short[] replacement)
    {
        var points = SourcePoints();
        (start, count) = RangeClampedIntoPoints(start, count);

        var splicedPoints = new short[points.Length - count + replacement.Length];
        points.AsSpan(0, start).CopyTo(splicedPoints);
        replacement.CopyTo(splicedPoints.AsSpan(start));
        points.AsSpan(start + count).CopyTo(splicedPoints.AsSpan(start + replacement.Length));

        return WithSourcePoints(splicedPoints, LoopPointAfterSplice(start, count, replacement.Length), Decoded.DoesLoop);
    }

    // The same sample with only a run of the source's points kept, re-encoded whole with the loop point following the audio it named, or held at the
    // nearer end of the run where that audio is gone.
    public Sample WithOnlyPointsKept(int start, int count)
    {
        (start, count) = RangeClampedIntoPoints(start, count);
        return WithSourcePoints(PointsInRange(start, count), Math.Clamp(Decoded.LoopStartPoint - start, 0, count), Decoded.DoesLoop);
    }

    // The same sample with a run of the source's points written over by as many others, re-encoded whole with the loop point where it was.
    public Sample WithPointsOverwritten(int start, short[] replacement)
    {
        var points = SourcePoints();
        (start, int count) = RangeClampedIntoPoints(start, replacement.Length);
        replacement.AsSpan(0, count).CopyTo(points.AsSpan(start));

        return WithSourcePoints(points, Decoded.LoopStartPoint, Decoded.DoesLoop);
    }

    // The same sample cut off at a loop's start, the loop's points put after it and looping from there.
    public Sample WithLoopFrom(int loopStartPoint, short[] loopPoints)
    {
        return WithSourcePoints([.. PointsInRange(0, loopStartPoint), .. loopPoints], loopStartPoint, doesLoop: true);
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

    // Whether another sample holds the same source under the same effects at the same rates, and so plays the same audio.
    public bool PlaysSameAudioAs(Sample other)
    {
        return SourceBytes.AsSpan().SequenceEqual(other.SourceBytes) && Effects.IsSameAs(other.Effects) && OriginalRateHz == other.OriginalRateHz && RateHz == other.RateHz;
    }

    // The same sample under another name, its extension kept, or this one where it is called that already.
    public Sample WithName(string name)
    {
        string renamedFileName = name + Path.GetExtension(FileName);
        return renamedFileName == FileName ? this : this with { FileName = renamedFileName };
    }

    // The same sample over a different source, its effects applied over it again and the result measured again.
    private Sample WithAudio(byte[] sourceBytes)
    {
        return Remeasured(Remade(sourceBytes, Effects));
    }

    // The same sample over new source points, encoded whole with the loop starting at the block nearest a point and the loop flag as given.
    private Sample WithSourcePoints(short[] points, int loopPoint, bool doesLoop)
    {
        if (!doesLoop)
        {
            return WithAudio(BrrEncoder.Encode(points, NearestBlockStart(loopPoint), doesLoop));
        }

        (points, loopPoint) = WithLoopPointOnBlockStart(points, loopPoint);
        if (Decoded.DoesLoop && points.AsSpan(loopPoint).SequenceEqual(SourcePoints().AsSpan(Decoded.LoopStartPoint)))
        {
            var sourceLoopBlocks = BrrSample.BlockBytes(SourceBytes, FileName).AsSpan(Decoded.LoopStartPoint / BrrSample.PointsPerBlock * BrrSample.BlockSize);
            return WithAudio(BrrEncoder.EncodeInFrontOfLoop(points[..loopPoint], sourceLoopBlocks));
        }

        return WithAudio(BrrEncoder.Encode(points, loopPoint, doesLoop));
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

        int firstSoundingPoint = points.AsSpan(0, loopPoint).IndexOfAnyExcept((short)0);
        int leadingSilentPoints = firstSoundingPoint < 0 ? loopPoint : firstSoundingPoint;
        if (leadingSilentPoints >= pointsPastBlockStart)
        {
            return (points[pointsPastBlockStart..], loopPoint - pointsPastBlockStart);
        }

        int addedSilentPoints = BrrSample.PointsPerBlock - pointsPastBlockStart;
        var padded = new short[addedSilentPoints + points.Length];
        points.CopyTo(padded, addedSilentPoints);
        return (padded, loopPoint + addedSilentPoints);
    }

    // The same sample under different effects, applied over its source again and the result measured again, or this one where it has those effects already.
    public Sample WithEffects(SampleRack effects)
    {
        return effects.IsSameAs(Effects) ? this : Remeasured(Remade(SourceBytes, effects));
    }

    // The same sample over a source and effects, what it plays made again from the two and the measurement carried over: the source's own bytes where
    // nothing changes the audio, or else its points through the effects, encoded under its loop.
    private Sample Remade(byte[] sourceBytes, SampleRack effects)
    {
        var source = BrrSample.Decode(sourceBytes, FileName);
        var withSourceAndEffects = this with { SourceBytes = sourceBytes, Effects = effects };
        if (!effects.DoesChangeAudioAt(RateHz))
        {
            return withSourceAndEffects with { FileBytes = sourceBytes, Decoded = source };
        }

        byte[] fileBytes = BrrEncoder.Encode(effects.Apply(source.Points, RateHz), source.LoopStartPoint, source.DoesLoop);
        return withSourceAndEffects with { FileBytes = fileBytes, Decoded = BrrSample.Decode(fileBytes, FileName) };
    }

    // The pitch of what the sample plays, measured again and given at its original rate.
    public BrrPitch.Measurement MeasuredPitch()
    {
        return BrrPitch.Estimate(Decoded).AtRateFraction(OriginalRateFraction / RateFractionOfOriginal);
    }

    // A sample with the pitch measured again from what it plays.
    private static Sample Remeasured(Sample sample)
    {
        return sample with { Pitch = sample.MeasuredPitch() };
    }

    // The source's audio, decoded.
    private short[] SourcePoints()
    {
        return BrrSample.Decode(SourceBytes, FileName).Points;
    }

    // A range of points held inside the sample: its start within the points, and its count within what is left after that start.
    private (int Start, int Count) RangeClampedIntoPoints(int start, int count)
    {
        int pointCount = PointCount;
        int clampedStart = Math.Clamp(start, 0, pointCount);
        return (clampedStart, Math.Clamp(count, 0, pointCount - clampedStart));
    }

    // Where the loop point lands once a splice has removed some points and inserted others: moved by the difference past the splice, and to its start inside the points removed.
    private int LoopPointAfterSplice(int start, int removedCount, int insertedCount)
    {
        int loopPoint = Decoded.LoopStartPoint;
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
