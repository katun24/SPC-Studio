namespace SpcStudio.Audio;

// Finds the loop inside a run of a sample's points whose jump from its end back to its start is smoothest, both ends on block starts: the smoothest of
// all, or of the loops that match about as well as the best one, the shortest.
public static class LoopFinder
{
    private const int SeamSidePoints = 1024;
    private const double AboutAsSmoothMargin = 0.01;
    private const double ExactlyAsSmoothMargin = 0;
    private const int PointsPerBlock = BrrSample.PointsPerBlock;
    private const int FirstLengthIndex = 1;

    // Whether a run of points, from its first point to the boundary past its last, holds at least one whole block for a loop.
    public static bool CanFindIn(int firstPoint, int endPoint)
    {
        return LastBlockStartAtOrBefore(endPoint) - FirstBlockStartAtOrAfter(firstPoint) >= PointsPerBlock;
    }

    // The shortest of the loops inside a run of points about as smooth as the smoothest, as its start point and the boundary past its last point; the
    // run must hold one whole block.
    public static (int StartPoint, int EndPoint) FindShortestAboutAsSmooth(short[] points, int firstPoint, int endPoint)
    {
        return FindShortestWithinMargin(points, firstPoint, endPoint, AboutAsSmoothMargin);
    }

    // The smoothest loop inside a run of points, as its start point and the boundary past its last point; the run must hold one whole block.
    public static (int StartPoint, int EndPoint) FindSmoothest(short[] points, int firstPoint, int endPoint)
    {
        return FindShortestWithinMargin(points, firstPoint, endPoint, ExactlyAsSmoothMargin);
    }

    // The shortest of the loops inside a run of points whose score comes within a margin of the smoothest's, as its start point and the boundary past
    // its last point.
    private static (int StartPoint, int EndPoint) FindShortestWithinMargin(short[] points, int firstPoint, int endPoint, double marginBelowBest)
    {
        int firstStart = FirstBlockStartAtOrAfter(firstPoint);
        int lastEnd = LastBlockStartAtOrBefore(Math.Min(endPoint, points.Length));
        var seam = new SeamPoints(points, Math.Max(0, firstStart - SeamSidePoints), Math.Min(points.Length, lastEnd + SeamSidePoints));
        var bestScores = new double[(lastEnd - firstStart) / PointsPerBlock + 1];
        var bestStarts = new int[bestScores.Length];

        for (int lengthIndex = FirstLengthIndex; lengthIndex < bestScores.Length; lengthIndex++)
        {
            int length = lengthIndex * PointsPerBlock;
            var laggedProductSums = seam.LaggedProductSums(length);
            bestScores[lengthIndex] = double.MinValue;
            for (int start = firstStart; start + length <= lastEnd; start += PointsPerBlock)
            {
                double score = seam.Score(start, length, laggedProductSums);
                if (score > bestScores[lengthIndex])
                {
                    bestScores[lengthIndex] = score;
                    bestStarts[lengthIndex] = start;
                }
            }
        }

        int chosenIndex = ShortestWithinMargin(bestScores, marginBelowBest);
        return (bestStarts[chosenIndex], bestStarts[chosenIndex] + chosenIndex * PointsPerBlock);
    }

    // The index of the shortest loop length whose best score comes within a margin of the best score of all.
    private static int ShortestWithinMargin(double[] bestScores, double marginBelowBest)
    {
        double threshold = bestScores.Skip(FirstLengthIndex).Max() - marginBelowBest;
        int lengthIndex = FirstLengthIndex;
        while (bestScores[lengthIndex] < threshold)
        {
            lengthIndex++;
        }

        return lengthIndex;
    }

    // The first block start at or after a point.
    private static int FirstBlockStartAtOrAfter(int point)
    {
        return (point + PointsPerBlock - 1) / PointsPerBlock * PointsPerBlock;
    }

    // The last block start at or before a point.
    private static int LastBlockStartAtOrBefore(int point)
    {
        return point / PointsPerBlock * PointsPerBlock;
    }

    // The points a search can compare across a seam, from the first a seam can reach back to up to the last it can reach forward to, with the running
    // sum of their energy.
    private sealed class SeamPoints
    {
        private readonly short[] _points;
        private readonly int _firstPoint;
        private readonly int _endPoint;
        private readonly double[] _energySums;

        // Takes the points between a first point and the boundary past the last that a seam can reach, and sums their energy.
        public SeamPoints(short[] points, int firstPoint, int endPoint)
        {
            _points = points;
            _firstPoint = firstPoint;
            _endPoint = endPoint;
            _energySums = RunningSums.OfEnergy(points, firstPoint, endPoint);
        }

        // The running sum of each point's product with the point a loop's length later, over every point that has one inside the reach.
        public double[] LaggedProductSums(int length)
        {
            return RunningSums.Of(_endPoint - _firstPoint - length, offset => (double)_points[_firstPoint + offset] * _points[_firstPoint + offset + length]);
        }

        // How alike the points around a loop's start are to those around its end, the points past the end being what the jump back stands in for.
        public double Score(int start, int length, double[] laggedProductSums)
        {
            int firstCompared = start - Math.Min(SeamSidePoints, start - _firstPoint) - _firstPoint;
            int endCompared = start + Math.Min(SeamSidePoints, _endPoint - (start + length)) - _firstPoint;
            double summedProducts = laggedProductSums[endCompared] - laggedProductSums[firstCompared];
            double startEnergy = _energySums[endCompared] - _energySums[firstCompared];
            double endEnergy = _energySums[endCompared + length] - _energySums[firstCompared + length];
            return NormalizedCorrelation.Of(summedProducts, startEnergy, endEnergy);
        }
    }
}
