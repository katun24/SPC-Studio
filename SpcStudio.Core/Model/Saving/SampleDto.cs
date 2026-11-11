using SpcStudio.Audio;

namespace SpcStudio.Model;

// A sample as both saved files write it: its file name, its source's bytes verbatim, its effects, the measurement an instrument's tuning is seeded from, the
// register the Sample Editor was set to play it at, the rate it was first made at and the rate its source was converted to.
internal class SampleDto
{
    public string FileName { get; set; } = "";
    public byte[] Brr { get; set; } = [];
    public List<SampleEffectDto>? Effects { get; set; }
    public double MeasuredHz { get; set; }
    public double PeriodSamples { get; set; }
    public double Confidence { get; set; }
    public int? PreviewPitchRegister { get; set; }
    public int? SourceRateHz { get; set; }
    public int? ConvertedRateHz { get; set; }
    public int? RateHz { get; set; }

    // Fills these fields in from a sample, leaving out a rate that is the one it would be read back as.
    public void CopyFrom(Sample sample)
    {
        FileName = sample.FileName;
        Brr = sample.SourceBytes;
        Effects = SampleEffectDto.ListOf(sample.Effects, sample.RateHz);
        MeasuredHz = sample.Pitch.FrequencyHz;
        PeriodSamples = sample.Pitch.PeriodPoints;
        Confidence = sample.Pitch.Confidence;
        PreviewPitchRegister = sample.ChosenPreviewPitchRegister;
        SourceRateHz = sample.OriginalRateHz == Sample.BrrSourceRateHz ? null : sample.OriginalRateHz;
        ConvertedRateHz = sample.RateHz == sample.OriginalRateHz ? null : sample.RateHz;
    }

    // The sample these fields describe, under an id, refusing effects it could not hold, and converted where an older file set it to play at a rate its
    // stored source is not at.
    public Sample ToSample(int id, string where)
    {
        int originalRateHz = SourceRateHz ?? Sample.BrrSourceRateHz;
        int rateHz = ConvertedRateHz ?? originalRateHz;
        var sample = Sample.FromSaved(id, FileName, Brr, SampleEffectDto.RackOf(Effects, where, rateHz), new BrrPitch.Measurement(MeasuredHz, PeriodSamples, Confidence),
            originalRateHz, rateHz);
        if (RateHz is int olderFileRateHz)
        {
            sample = sample.ConvertedTo(olderFileRateHz);
        }

        return PreviewPitchRegister is int pitchRegister ? sample.WithPreviewPitchRegister(pitchRegister) : sample;
    }
}
