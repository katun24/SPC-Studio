namespace SpcStudio.Audio;

// Finds the loop inside a run of a sample's points whose jump from its end back to its start is smoothest, its length whole blocks and its start on a
// block or on any point: the smoothest of all, or of the loops that match about as well as the best one, the shortest. Also measures the length, a
// fraction of a point included, that a loop's sound repeats at.
public static class LoopFinder
{
    public const int BlockStartStepPoints = BrrSample.PointsPerBlock;
    public const int EveryPointStepPoints = 1;
    private const int SeamSidePoints = 1024;
    private const double AboutAsSmoothMargin = 0.01;
    private const double ExactlyAsSmoothMargin = 0;
    private const int PointsPerBlock = BrrSample.PointsPerBlock;
    private const int FirstLengthIndex = 1;
    private const int NeighborLengthPoints = 1;
    private const int RepeatSearchPoints = PointsPerBlock / 2;

    // Whether a run of points, from its first point to the boundary past its last, holds a loop of at least one block whose start and end are each a whole
    // number of steps of points from the sample's start.
    public static bool CanFindIn(int firstPoint, int endPoint, int startStepPoints)
    {
        return LastStepPointAtOrBefore(endPoint, startStepPoints) - FirstStepPointAtOrAfter(firstPoint, startStepPoints) >= PointsPerBlock;
    }

    // The shortest of the loops inside a run of points about as smooth as the smoothest, its start tried at every step of points; the run must hold a loop.
    public static FoundLoop FindShortestAboutAsSmooth(short[] points, int firstPoint, int endPoint, int startStepPoints)
    {
        return FindShortestWithinMargin(points, firstPoint, endPoint, startStepPoints, AboutAsSmoothMargin);
    }

    // The smoothest loop inside a run of points, its start tried at every step of points; the run must hold a loop.
    public static FoundLoop FindSmoothest(short[] points, int firstPoint, int endPoint, int startStepPoints)
    {
        return FindShortestWithinMargin(points, firstPoint, endPoint, startStepPoints, ExactlyAsSmoothMargin);
    }

    // The length in points, a fraction of a point included, that the sound of a loop between a start point and the boundary past its last point
    // repeats at, or none where the points before its start and past its end are too few to measure it.
    public static double? RepeatPointsOf(short[] points, int startPoint, int endPoint)
    {
        var seam = new SeamPoints(points, Math.Max(0, startPoint - SeamSidePoints), Math.Min(points.Length, endPoint + SeamSidePoints));
        return RepeatPointsAt(seam, startPoint, endPoint - startPoint);
    }

    // The shortest of the loops inside a run of points, its start tried at every step of points, whose score comes within a margin of the smoothest's.
    private static FoundLoop FindShortestWithinMargin(short[] points, int firstPoint, int endPoint, int startStepPoints, double marginBelowBest)
    {
        int firstStart = FirstStepPointAtOrAfter(firstPoint, startStepPoints);
        int lastEnd = LastStepPointAtOrBefore(Math.Min(endPoint, points.Length), startStepPoints);
        var seam = new SeamPoints(points, Math.Max(0, firstStart - SeamSidePoints), Math.Min(points.Length, lastEnd + SeamSidePoints));
        var bestScores = new double[(lastEnd - firstStart) / PointsPerBlock + 1];
        var bestStarts = new int[bestScores.Length];

        for (int lengthIndex = FirstLengthIndex; lengthIndex < bestScores.Length; lengthIndex++)
        {
            int length = lengthIndex * PointsPerBlock;
            var laggedProductSums = seam.LaggedProductSums(length);
            bestScores[lengthIndex] = double.MinValue;
            for (int start = firstStart; start + length <= lastEnd; start += startStepPoints)
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
        int chosenStart = bestStarts[chosenIndex];
        int chosenLength = chosenIndex * PointsPerBlock;
        return new FoundLoop(chosenStart, chosenStart + chosenLength, RepeatPointsAt(seam, chosenStart, chosenLength) ?? chosenLength);
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

    // The length, a fraction of a point included, that the sound of a loop repeats at - the length within half a block of its own that scores best,
    // moved to where the scores of that length and of one point either side of it peak - or none where the seam's points cannot score the longest.
    private static double? RepeatPointsAt(SeamPoints seam, int start, int length)
    {
        int shortestScoredLength = length - RepeatSearchPoints - NeighborLengthPoints;
        int longestScoredLength = length + RepeatSearchPoints + NeighborLengthPoints;
        if (!seam.CanScore(longestScoredLength))
        {
            return null;
        }

        var lengthScores = new double[longestScoredLength - shortestScoredLength + 1];
        for (int i = 0; i < lengthScores.Length; i++)
        {
            lengthScores[i] = seam.Score(start, shortestScoredLength + i, seam.LaggedProductSums(shortestScoredLength + i));
        }

        int bestLengthIndex = NeighborLengthPoints;
        for (int i = bestLengthIndex + 1; i < lengthScores.Length - NeighborLengthPoints; i++)
        {
            if (lengthScores[i] > lengthScores[bestLengthIndex])
            {
                bestLengthIndex = i;
            }
        }

        double peakOffset = ParabolicPeak.OffsetOf(lengthScores[bestLengthIndex - 1], lengthScores[bestLengthIndex], lengthScores[bestLengthIndex + 1]);
        return shortestScoredLength + bestLengthIndex + peakOffset;
    }

    // The first point at or after a point that is a whole number of steps of points from the sample's start.
    private static int FirstStepPointAtOrAfter(int point, int stepPoints)
    {
        return (point + stepPoints - 1) / stepPoints * stepPoints;
    }

    // The last point at or before a point that is a whole number of steps of points from the sample's start.
    private static int LastStepPointAtOrBefore(int point, int stepPoints)
    {
        return point / stepPoints * stepPoints;
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

        // Whether the reach holds more points than a loop's length, so that a seam of that length has at least one point either side of it to compare.
        public bool CanScore(int length)
        {
            return _endPoint - _firstPoint > length;
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
