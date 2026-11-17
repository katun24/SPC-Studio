namespace SpcStudio.Audio;

// A loop to be fitted to whole blocks by lowering its sample's rate: the point it starts at, and the length in points, a fraction of a point included,
// that its sound repeats at.
public readonly record struct LoopFit(int StartPoint, double RepeatPoints)
{
    // How many whole blocks the loop is fitted into: as many as its repeat length holds.
    public int BlockCount
    {
        get
        {
            return (int)Math.Floor(RepeatPoints / BrrSample.PointsPerBlock);
        }
    }

    // How many points the fitted loop holds.
    public int FittedPointCount
    {
        get
        {
            return BlockCount * BrrSample.PointsPerBlock;
        }
    }

    // The fraction of the sample's rate that turns the loop's repeat length into exactly its whole blocks.
    public double RateFraction
    {
        get
        {
            return FittedPointCount / RepeatPoints;
        }
    }
}
