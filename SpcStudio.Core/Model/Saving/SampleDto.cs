using System.IO;
using SpcStudio.Audio;

namespace SpcStudio.Model;

// A sample as both saved files write it: its file name, its kept source - the .brr it arrived as verbatim, or else its points and its loop - its effects,
// the measurement an instrument's tuning is seeded from and whether edits keep it, the register the Sample Editor was set to play it at, the rate it was
// first made at, the rate its kept source is at and the rate the Rate slider is set to.
internal class SampleDto
{
    public string FileName { get; set; } = "";
    public byte[]? Brr { get; set; }
    public byte[]? PointBytes { get; set; }
    public int LoopStartPoint { get; set; }
    public bool DoesLoop { get; set; }
    public List<SampleEffectDto>? Effects { get; set; }
    public double MeasuredHz { get; set; }
    public double PeriodSamples { get; set; }
    public double Confidence { get; set; }
    public bool? IsPitchKept { get; set; }
    public int? PreviewPitchRegister { get; set; }
    public int? SourceRateHz { get; set; }
    public int? ConvertedRateHz { get; set; }
    public int? RateHz { get; set; }

    // Fills these fields in from a sample, leaving out a rate that is the one it would be read back as, and the kept measurement where edits do not keep it.
    public void CopyFrom(Sample sample)
    {
        FileName = sample.FileName;
        CopyKeptSourceFrom(sample);
        Effects = SampleEffectDto.ListOf(sample.Effects, sample.KeptRateHz);
        MeasuredHz = sample.Pitch.FrequencyHz;
        PeriodSamples = sample.Pitch.PeriodPoints;
        Confidence = sample.Pitch.Confidence;
        IsPitchKept = sample.IsPitchKeptThroughEdits ? true : null;
        PreviewPitchRegister = sample.ChosenPreviewPitchRegister;
        SourceRateHz = sample.OriginalRateHz == Sample.BrrSourceRateHz ? null : sample.OriginalRateHz;
        ConvertedRateHz = sample.KeptRateHz == sample.OriginalRateHz ? null : sample.KeptRateHz;
        RateHz = sample.RateHz == sample.KeptRateHz ? null : sample.RateHz;
    }

    // The sample these fields describe, under an id, refusing a source or effects it could not hold, and converted from its kept source where the Rate
    // slider was set below the rate that source is at.
    public Sample ToSample(int id, string where)
    {
        int originalRateHz = SourceRateHz ?? Sample.BrrSourceRateHz;
        int keptRateHz = ConvertedRateHz ?? originalRateHz;
        var (keptSource, arrivedBrrBytes) = PointBytes is byte[] pointBytes ? (SourceFromPoints(pointBytes, where), null) : (BrrSample.Decode(Brr ?? [], FileName), Brr);
        var sample = Sample.FromSaved(id, FileName, keptSource, arrivedBrrBytes, SampleEffectDto.RackOf(Effects, where, keptRateHz),
            new BrrPitch.Measurement(MeasuredHz, PeriodSamples, Confidence), originalRateHz, keptRateHz);
        sample = sample with { IsPitchKeptThroughEdits = IsPitchKept == true };
        if (RateHz is int rateHz)
        {
            sample = sample.ConvertedTo(rateHz);
        }

        return PreviewPitchRegister is int pitchRegister ? sample.WithPreviewPitchRegister(pitchRegister) : sample;
    }

    // Writes the sample's kept source: the .brr it arrived as where it still holds it, or else its points and its loop.
    private void CopyKeptSourceFrom(Sample sample)
    {
        if (sample.ArrivedBrrBytes is byte[] arrivedBrrBytes)
        {
            Brr = arrivedBrrBytes;
            return;
        }

        PointBytes = DspPoint.ToLittleEndianBytes(sample.KeptSource.Points);
        LoopStartPoint = sample.KeptSource.LoopStartPoint;
        DoesLoop = sample.KeptSource.DoesLoop;
    }

    // The kept source written as the bytes of its points and as its loop, refusing points that are not whole blocks and a loop that does not start on one of them.
    private BrrSample SourceFromPoints(byte[] pointBytes, string where)
    {
        var points = DspPoint.FromLittleEndianBytes(pointBytes);
        var source = BrrSample.FromPoints(points, LoopStartPoint, DoesLoop);
        if (pointBytes.Length % DspPoint.BytesPerPoint != 0 || source.Points.Length != points.Length || source.LoopStartPoint != LoopStartPoint)
        {
            throw new InvalidDataException($"{where} has points that are not whole blocks, or a loop that does not start on one.");
        }

        return source;
    }
}
