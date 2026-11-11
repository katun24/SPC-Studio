namespace SpcStudio.Audio;

// How far into each sample the voices it is handed to have read, and the highest PITCH register they read it at.
public sealed class SampleReachLog
{
    private readonly Dictionary<BrrSample, SampleReach> _reachBySample = new(ReferenceEqualityComparer.Instance);

    // Takes one read of a sample: a point a voice has reached in it at a PITCH register.
    public void Record(BrrSample sample, int point, int pitchRegister)
    {
        var reach = _reachBySample.GetValueOrDefault(sample);
        _reachBySample[sample] = new SampleReach(Math.Max(reach.FurthestPoint, point), Math.Max(reach.HighestPitchRegister, pitchRegister));
    }

    // Takes a sample as read to its end, for a voice whose reading of it cannot be followed.
    public void RecordWhole(BrrSample sample)
    {
        Record(sample, sample.Points.Length, 0);
    }

    // How far the voices read a sample, or null where none read it at all.
    public SampleReach? ReachOf(BrrSample sample)
    {
        return _reachBySample.TryGetValue(sample, out var reach) ? reach : null;
    }

    // The furthest point the voices reached in a sample and the highest PITCH register they read it at.
    public readonly record struct SampleReach(int FurthestPoint, int HighestPitchRegister);
}
