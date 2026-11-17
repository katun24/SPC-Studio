namespace SpcStudio.Audio;

// Audio to BRR blocks: for each block, every range and filter the header can name scored against what the decoder would reconstruct, the winner's two history points carried into the next block.
public static class BrrEncoder
{
    public const int CandidateHeaderCount = RangeCount * BrrSample.FilterCount;
    private const int RangeCount = BrrSample.MaxShift + 1;
    private const int FilterThatPredictsNothing = 0;
    private const int FirstBlock = 0;
    private const int LoopPasses = 2;

    // Audio as a complete AddmusicK .brr: the loop header, then the blocks, with END on the last one and the loop flag beside it where the sample loops,
    // a loop's blocks encoded for both ways into them.
    public static byte[] Encode(short[] points, int loopPoint = 0, bool doesLoop = false, Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        int blockCount = BrrSample.BlockCountFor(points.Length);
        int loopBlock = BrrSample.LoopBlockOf(loopPoint, blockCount);
        var blocks = new byte[blockCount * BrrSample.BlockSize];
        var loopEntryHistory = EncodeBlocks(points, blocks, FirstBlock, loopBlock, loopBlock, [default], progress, cancel);
        var endHistory = EncodeBlocks(points, blocks, loopBlock, blockCount, loopBlock, [loopEntryHistory], progress, cancel);

        if (doesLoop && loopBlock != FirstBlock)
        {
            for (int pass = 0; pass < LoopPasses; pass++)
            {
                endHistory = EncodeBlocks(points, blocks, loopBlock, blockCount, loopBlock, [loopEntryHistory, endHistory], null, cancel);
            }
        }

        return BrrSample.FileBytesFromBlocks(blocks, loopBlock, doesLoop);
    }

    // A sample's audio as a complete AddmusicK .brr, under its own loop.
    public static byte[] Encode(BrrSample audio, Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        return Encode(audio.Points, audio.LoopStartPoint, audio.DoesLoop, progress, cancel);
    }

    // Encodes a run of blocks, every candidate scored over each way into the block at once, and answers the history the last way in leaves behind.
    private static BrrHistory EncodeBlocks(short[] points, byte[] blocks, int firstBlock, int endBlock, int loopBlock, ReadOnlySpan<BrrHistory> waysIn,
        Action<int, int>? progress, CancellationToken cancel)
    {
        int blockCount = blocks.Length / BrrSample.BlockSize;
        var blockTarget = new short[BrrSample.PointsPerBlock];
        var halvedTargets = new int[BrrSample.PointsPerBlock];
        var bestResiduals = new int[BrrSample.PointsPerBlock];
        Span<BrrHistory> histories = stackalloc BrrHistory[waysIn.Length];
        Span<int> candidateOrder = stackalloc int[CandidateHeaderCount];
        waysIn.CopyTo(histories);

        for (int block = firstBlock; block < endBlock; block++)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Invoke(block, blockCount);

            int firstPoint = block * BrrSample.PointsPerBlock;
            for (int point = 0; point < BrrSample.PointsPerBlock; point++)
            {
                blockTarget[point] = firstPoint + point < points.Length ? points[firstPoint + point] : (short)0;
                halvedTargets[point] = HalvedAwayFromZero(blockTarget[point]);
            }

            // The last filter the block may use, which is the one that predicts nothing at key-on and at a loop block whose second way in is not known yet.
            int lastFilter = block == FirstBlock || (block == loopBlock && histories.Length == 1) ? FilterThatPredictsNothing : BrrSample.FilterCount - 1;

            var blockCandidateOrder = candidateOrder[..((lastFilter + 1) * RangeCount)];
            OrderCandidates(blockCandidateOrder, halvedTargets, histories[0]);
            int bestCandidate = BestCandidate(blockCandidateOrder, blockTarget, halvedTargets, histories, bestResiduals);

            int blockOffset = block * BrrSample.BlockSize;
            blocks[blockOffset] = BrrSample.HeaderByte(ShiftOf(bestCandidate), FilterOf(bestCandidate));
            BrrSample.WriteResiduals(blocks, blockOffset, bestResiduals);
        }

        return histories[^1];
    }

    // Fills in the order a block's candidates are scored in: each filter at the range its largest step asks for first, then the rest in header order.
    private static void OrderCandidates(Span<int> candidateOrder, int[] halvedTargets, BrrHistory wayIn)
    {
        int filterCount = candidateOrder.Length / RangeCount;
        for (int filter = 0; filter < filterCount; filter++)
        {
            candidateOrder[filter] = CandidateOf(filter, SmallestRangeHolding(LargestStep(halvedTargets, filter, wayIn)));
        }

        int nextSlot = filterCount;
        for (int candidate = 0; candidate < candidateOrder.Length; candidate++)
        {
            if (candidate == candidateOrder[FilterOf(candidate)])
            {
                continue;
            }

            candidateOrder[nextSlot++] = candidate;
        }
    }

    // Scores a block's candidates in the order given, each given up once it can no longer win, and answers the best one, its residuals filled in and
    // every history carried past the block on it; a tie goes to the candidate first in header order, as scoring them in that order would choose.
    private static int BestCandidate(ReadOnlySpan<int> candidateOrder, short[] blockTarget, int[] halvedTargets, Span<BrrHistory> histories, int[] bestResiduals)
    {
        Span<BrrHistory> candidateHistories = stackalloc BrrHistory[histories.Length];
        Span<BrrHistory> bestHistories = stackalloc BrrHistory[histories.Length];
        Span<int> candidateResiduals = stackalloc int[BrrSample.PointsPerBlock];
        long bestError = long.MaxValue;
        int bestCandidate = int.MaxValue;

        foreach (int candidate in candidateOrder)
        {
            histories.CopyTo(candidateHistories);
            long error = ScoreBlock(blockTarget, halvedTargets, ShiftOf(candidate), FilterOf(candidate), candidateHistories, candidateResiduals, bestError);
            bool beatsBest = error < bestError || (error == bestError && candidate < bestCandidate);
            if (!beatsBest)
            {
                continue;
            }

            bestError = error;
            bestCandidate = candidate;
            candidateHistories.CopyTo(bestHistories);
            candidateResiduals.CopyTo(bestResiduals);
        }

        bestHistories.CopyTo(histories);
        return bestCandidate;
    }

    // The best one range and filter can do for one block reached by each of a set of histories: fills in the residuals it chose, carries every history
    // to the block's end, and answers the squared error against the target summed over all of them - given up as it stands once it passes the error to beat.
    private static long ScoreBlock(short[] blockTarget, int[] halvedTargets, int shift, int filter, Span<BrrHistory> histories, Span<int> residuals, long errorToBeat)
    {
        long blockError = 0;
        Span<BrrHistory> candidateHistories = stackalloc BrrHistory[histories.Length];
        Span<BrrHistory> chosenHistories = stackalloc BrrHistory[histories.Length];

        for (int point = 0; point < blockTarget.Length; point++)
        {
            long pointError = long.MaxValue;
            int chosen = 0;

            foreach (var history in histories)
            {
                int estimate = Math.Clamp(EstimateResidual(StepPastPrediction(halvedTargets[point], filter, history), shift), BrrSample.MinResidual, BrrSample.MaxResidual);
                for (int residual = estimate - 1; residual <= estimate + 1; residual++)
                {
                    if (residual < BrrSample.MinResidual || residual > BrrSample.MaxResidual)
                    {
                        continue;
                    }

                    long candidateError = SummedError(residual, shift, filter, blockTarget[point], histories, candidateHistories);
                    if (candidateError >= pointError)
                    {
                        continue;
                    }

                    pointError = candidateError;
                    chosen = residual;
                    candidateHistories.CopyTo(chosenHistories);
                }
            }

            residuals[point] = chosen;
            blockError += pointError;
            chosenHistories.CopyTo(histories);
            if (blockError > errorToBeat)
            {
                return blockError;
            }
        }

        return blockError;
    }

    // The squared error one residual decodes to against a target, summed over each history it is reached by, every history carried past the point into
    // its own slot of the histories after.
    private static long SummedError(int residual, int shift, int filter, short target, ReadOnlySpan<BrrHistory> histories, Span<BrrHistory> historiesAfter)
    {
        long error = 0;
        for (int index = 0; index < histories.Length; index++)
        {
            var history = histories[index];
            long difference = BrrSample.DecodePoint(residual, shift, filter, ref history) - target;
            error += difference * difference;
            historiesAfter[index] = history;
        }

        return error;
    }

    // The largest step a block takes past its filter's prediction, each point predicted from the block's own points before it rather than from a decode.
    private static int LargestStep(int[] halvedTargets, int filter, BrrHistory wayIn)
    {
        var history = wayIn;
        int largestStep = 0;
        foreach (int halvedTarget in halvedTargets)
        {
            largestStep = Math.Max(largestStep, Math.Abs(StepPastPrediction(halvedTarget, filter, history)));
            history = history.After(halvedTarget);
        }

        return largestStep;
    }

    // The finest range whose residuals reach a step.
    private static int SmallestRangeHolding(int step)
    {
        for (int shift = 0; shift < BrrSample.MaxShift; shift++)
        {
            if (EstimateResidual(step, shift) <= BrrSample.MaxResidual)
            {
                return shift;
            }
        }

        return BrrSample.MaxShift;
    }

    // How far a 15-bit point lies from what a filter predicts for it.
    private static int StepPastPrediction(int halvedTarget, int filter, BrrHistory history)
    {
        return halvedTarget - BrrSample.FilterPrediction(filter, history);
    }

    // The residual that would land closest to a wanted value at a range, before it is held to what four bits can hold.
    private static int EstimateResidual(int wanted, int shift)
    {
        int denominator = 1 << shift;
        int numerator = wanted * 2;

        return numerator >= 0 ? (numerator + denominator / 2) / denominator : (numerator - denominator / 2) / denominator;
    }

    // The 15-bit value whose doubled output is a point, rounded away from zero.
    private static int HalvedAwayFromZero(int point)
    {
        return point >= 0 ? (point + 1) / 2 : (point - 1) / 2;
    }

    // A candidate's place in header order, every range of the first filter before the next filter's.
    private static int CandidateOf(int filter, int shift)
    {
        return filter * RangeCount + shift;
    }

    // The filter a candidate names.
    private static int FilterOf(int candidate)
    {
        return candidate / RangeCount;
    }

    // The range a candidate names.
    private static int ShiftOf(int candidate)
    {
        return candidate % RangeCount;
    }
}
