namespace SpcStudio.Audio;

// Audio to BRR blocks: for each block, every range and filter the header can name scored against what the decoder would reconstruct, the winner's two history points carried into the next block.
public static class BrrEncoder
{
    public const int CandidateHeaderCount = (BrrSample.MaxShift + 1) * BrrSample.FilterCount;
    private const int FilterThatPredictsNothing = 0;
    private const int FirstBlock = 0;
    private const int LoopPasses = 2;

    // Audio as a complete AddmusicK .brr: the loop header, then the blocks, with END on the last one and the loop flag beside it where the sample loops,
    // a loop's blocks encoded for both ways into them.
    public static byte[] Encode(short[] points, int loopPoint = 0, bool doesLoop = false, Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        int blockCount = Math.Max(1, (points.Length + BrrSample.PointsPerBlock - 1) / BrrSample.PointsPerBlock);
        int loopBlock = Math.Clamp(loopPoint / BrrSample.PointsPerBlock, 0, blockCount - 1);
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

    // Audio in front of a loop encoded as the start of a looping .brr, whole blocks of it, with the loop's own blocks kept as they are after it.
    public static byte[] EncodeInFrontOfLoop(short[] pointsInFront, ReadOnlySpan<byte> loopBlocks)
    {
        int loopBlock = pointsInFront.Length / BrrSample.PointsPerBlock;
        var blocks = new byte[loopBlock * BrrSample.BlockSize + loopBlocks.Length];
        EncodeBlocks(pointsInFront, blocks, FirstBlock, loopBlock, loopBlock, [default], null, default);
        loopBlocks.CopyTo(blocks.AsSpan(loopBlock * BrrSample.BlockSize));
        return BrrSample.FileBytesFromBlocks(blocks, loopBlock, doesLoop: true);
    }

    // Encodes a run of blocks, every candidate scored over each way into the block at once, and answers the history the last way in leaves behind.
    private static BrrHistory EncodeBlocks(short[] points, byte[] blocks, int firstBlock, int endBlock, int loopBlock, ReadOnlySpan<BrrHistory> waysIn,
        Action<int, int>? progress, CancellationToken cancel)
    {
        int blockCount = blocks.Length / BrrSample.BlockSize;
        var blockTarget = new short[BrrSample.PointsPerBlock];
        var candidateResiduals = new int[BrrSample.PointsPerBlock];
        var bestResiduals = new int[BrrSample.PointsPerBlock];
        Span<BrrHistory> histories = stackalloc BrrHistory[waysIn.Length];
        Span<BrrHistory> candidateHistories = stackalloc BrrHistory[waysIn.Length];
        Span<BrrHistory> bestHistories = stackalloc BrrHistory[waysIn.Length];
        waysIn.CopyTo(histories);

        for (int block = firstBlock; block < endBlock; block++)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Invoke(block, blockCount);

            int firstPoint = block * BrrSample.PointsPerBlock;
            for (int point = 0; point < BrrSample.PointsPerBlock; point++)
            {
                blockTarget[point] = firstPoint + point < points.Length ? points[firstPoint + point] : (short)0;
            }

            long bestError = long.MaxValue;
            byte bestHeader = 0;
            histories.CopyTo(bestHistories);

            // The last filter the block may use, which is the one that predicts nothing at key-on and at a loop block whose second way in is not known yet.
            int lastFilter = block == FirstBlock || (block == loopBlock && histories.Length == 1) ? FilterThatPredictsNothing : BrrSample.FilterCount - 1;

            for (int filter = 0; filter <= lastFilter; filter++)
            {
                for (int shift = 0; shift <= BrrSample.MaxShift; shift++)
                {
                    histories.CopyTo(candidateHistories);
                    long error = ScoreBlock(blockTarget, shift, filter, candidateHistories, candidateResiduals);
                    if (error >= bestError)
                    {
                        continue;
                    }

                    bestError = error;
                    bestHeader = BrrSample.HeaderByte(shift, filter);
                    candidateHistories.CopyTo(bestHistories);
                    Array.Copy(candidateResiduals, bestResiduals, BrrSample.PointsPerBlock);
                }
            }

            int blockOffset = block * BrrSample.BlockSize;
            blocks[blockOffset] = bestHeader;
            BrrSample.WriteResiduals(blocks, blockOffset, bestResiduals);

            bestHistories.CopyTo(histories);
        }

        return histories[^1];
    }

    // The best one range and filter can do for one block reached by each of a set of histories: fills in the residuals it chose, carries every history
    // to the block's end, and answers the squared error against the target summed over all of them.
    private static long ScoreBlock(short[] blockTarget, int shift, int filter, Span<BrrHistory> histories, int[] residuals)
    {
        long blockError = 0;
        Span<BrrHistory> candidateHistories = stackalloc BrrHistory[histories.Length];
        Span<BrrHistory> chosenHistories = stackalloc BrrHistory[histories.Length];

        for (int point = 0; point < blockTarget.Length; point++)
        {
            int halvedTarget = HalvedAwayFromZero(blockTarget[point]);
            long pointError = long.MaxValue;
            int chosen = 0;

            foreach (var history in histories)
            {
                int estimate = Math.Clamp(EstimateResidual(halvedTarget - BrrSample.FilterPrediction(filter, history), shift), BrrSample.MinResidual, BrrSample.MaxResidual);
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
}
