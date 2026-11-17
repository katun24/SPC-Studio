namespace SpcStudio.Audio;

// Points copied out of a sample's source in the Sample Editor, with the rate they were at.
public sealed record CopiedPoints(short[] Points, int RateHz)
{
    // The points converted to a rate, or as they are where they are at that rate already.
    public short[] PointsAt(int rateHz)
    {
        return rateHz == RateHz ? Points : PointResampler.Resampled(Points, rateHz / (double)RateHz);
    }
}
