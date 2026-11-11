namespace SpcStudio.Audio;

// The two 15-bit points a BRR block's filter predicts the next one from, silent where nothing has been decoded yet.
public readonly record struct BrrHistory(int PreviousPoint, int PointBeforePrevious)
{
    // The history once one more point has been decoded.
    public BrrHistory After(int point)
    {
        return new(point, PreviousPoint);
    }
}
