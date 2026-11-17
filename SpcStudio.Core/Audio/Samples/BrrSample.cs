using System.IO;
using System.Numerics;
using SpcStudio.Model;

namespace SpcStudio.Audio;

// A BRR sample's audio - its points in whole blocks, where it loops back to and whether it does, decoded from a .brr or held ready to encode - and the
// one copy of the DSP's arithmetic over a .brr's bytes: the loop header, a block's header byte, its residuals and the point each residual decodes to.
public sealed class BrrSample
{
    public const byte LoopFlag = 0x02;
    public const byte EndFlag = 0x01;
    public const int BlockSize = 9;
    public const int PointsPerBlock = 16;
    public const int LoopHeaderSize = 2;
    public const int MaxShift = 12;
    public const int FilterCount = 4;
    public const int MinResidual = -8;
    public const int MaxResidual = 7;
    public const string DefaultName = "sample";
    private const int NoHeaderSize = 0;
    private const int BlockHeaderSize = 1;
    private const int ShiftBit = 4;
    private const int FilterBit = 2;
    private const int FilterMask = FilterCount - 1;
    private const int ResidualBits = 4;
    private const int ResidualMask = (1 << ResidualBits) - 1;
    private const int ResidualRange = 1 << ResidualBits;
    private const int ResidualsPerByte = 2;
    private const int DegenerateNegativePoint = -2048;

    private BrrSample(short[] points, int loopStartPoint, bool doesLoop)
    {
        Points = points;
        LoopStartPoint = loopStartPoint;
        DoesLoop = doesLoop;
    }

    public short[] Points { get; }
    public int LoopStartPoint { get; }
    public bool DoesLoop { get; }

    // Audio as an encode takes it: the points padded with silence to whole blocks, never fewer than one, looping back to the start of the block a point is
    // in, held inside them.
    public static BrrSample FromPoints(short[] points, int loopPoint, bool doesLoop)
    {
        int blockCount = BlockCountFor(points.Length);
        int paddedPointCount = blockCount * PointsPerBlock;
        short[] wholeBlocks = points.Length == paddedPointCount ? points : [.. points, .. new short[paddedPointCount - points.Length]];
        return new BrrSample(wholeBlocks, LoopBlockOf(loopPoint, blockCount) * PointsPerBlock, doesLoop);
    }

    // How many blocks a run of points fills, the last one padded with silence, and never fewer than one.
    public static T BlockCountFor<T>(T pointCount) where T : IBinaryInteger<T>
    {
        var pointsPerBlock = T.CreateChecked(PointsPerBlock);
        return T.Max(T.One, (pointCount + pointsPerBlock - T.One) / pointsPerBlock);
    }

    // The first point of the first block starting at a point or after it.
    public static long BlockStartAtOrAfter(double point)
    {
        return (long)Math.Ceiling(point / PointsPerBlock) * PointsPerBlock;
    }

    // The block a loop starting at a point loops back to, held inside a sample of a number of blocks.
    public static int LoopBlockOf(int loopPoint, int blockCount)
    {
        return Math.Clamp(loopPoint / PointsPerBlock, 0, blockCount - 1);
    }

    // Whether another sample holds the same points, looping back to the same point and looping or not as this one does.
    public bool IsSameAs(BrrSample other)
    {
        return Points.AsSpan().SequenceEqual(other.Points) && LoopStartPoint == other.LoopStartPoint && DoesLoop == other.DoesLoop;
    }

    // The loop's points played over and over from its start, for a count of points.
    public short[] LoopPointsRepeatedFor(int pointCount)
    {
        int loopLength = Points.Length - LoopStartPoint;
        var repeated = new short[pointCount];
        for (int point = 0; point < pointCount; point++)
        {
            repeated[point] = Points[LoopStartPoint + point % loopLength];
        }

        return repeated;
    }

    // The points, read on past the last into the loop's start for a count of points more where the sample loops - what plays after them - or the points
    // alone where it does not.
    public short[] PointsReadOnIntoLoopFor(int morePointCount)
    {
        return DoesLoop ? [.. Points, .. LoopPointsRepeatedFor(morePointCount)] : Points;
    }

    // Decodes BRR data with or without AddmusicK's loop header, a file with none looping back to its start if its last block loops at all.
    public static BrrSample Decode(byte[] fileBytes, string name = DefaultName)
    {
        int headerSize = HeaderSize(fileBytes, name);
        int loopOffset = headerSize == LoopHeaderSize ? LoopOffsetOf(fileBytes) : 0;
        int blockCount = (fileBytes.Length - headerSize) / BlockSize;
        var points = new short[blockCount * PointsPerBlock];
        var history = default(BrrHistory);
        bool doesLoop = false;
        int pointIndex = 0;

        for (int block = 0; block < blockCount; block++)
        {
            int blockOffset = headerSize + block * BlockSize;
            byte header = fileBytes[blockOffset];
            doesLoop = (header & LoopFlag) != 0;

            for (int point = 0; point < PointsPerBlock; point++)
            {
                int residual = ResidualAt(fileBytes, blockOffset, point);
                points[pointIndex++] = DecodePoint(residual, ShiftOf(header), FilterOf(header), ref history);
            }
        }

        int loopStartPoint = Math.Clamp(loopOffset / BlockSize * PointsPerBlock, 0, points.Length - 1);
        return new BrrSample(points, loopStartPoint, doesLoop);
    }

    // How many bytes come before the block data - AddmusicK's loop header or none - refusing bytes that are neither layout.
    public static int HeaderSize(byte[] fileBytes, string name = DefaultName)
    {
        if (fileBytes.Length >= BlockSize + LoopHeaderSize && (fileBytes.Length - LoopHeaderSize) % BlockSize == 0)
        {
            return LoopHeaderSize;
        }

        if (fileBytes.Length >= BlockSize && fileBytes.Length % BlockSize == 0)
        {
            return NoHeaderSize;
        }

        throw new InvalidDataException(
            $"'{name}' is {fileBytes.Length} bytes, which is not {BlockSize}-byte BRR blocks either with or without a {LoopHeaderSize}-byte loop header.");
    }

    // How many blocks a .brr holds, whichever layout it is in.
    public static int BlockCount(byte[] fileBytes, string name = DefaultName)
    {
        return (fileBytes.Length - HeaderSize(fileBytes, name)) / BlockSize;
    }

    // The block data of a .brr with any loop header taken off.
    public static byte[] BlockBytes(byte[] fileBytes, string name = DefaultName)
    {
        return fileBytes[HeaderSize(fileBytes, name)..];
    }

    // The same audio cut off after a number of blocks, at least one, under AddmusicK's loop header: the last block kept ending the sample without a loop,
    // every byte before it as it was, and the loop point held inside what is kept.
    public static byte[] WithBlocksKept(byte[] fileBytes, int blockCount, string name = DefaultName)
    {
        int headerSize = HeaderSize(fileBytes, name);
        int keptBlockCount = Math.Clamp(blockCount, 1, BlockCount(fileBytes, name));
        var blocks = fileBytes.AsSpan(headerSize, keptBlockCount * BlockSize).ToArray();

        int lastHeader = (keptBlockCount - 1) * BlockSize;
        MarkLastBlock(blocks, lastHeader, doesLoop: false);

        int loopOffset = headerSize == LoopHeaderSize ? LoopOffsetOf(fileBytes) : 0;
        return WithLoopHeader(blocks, Math.Min(loopOffset, lastHeader));
    }

    // A .brr from block bytes: AddmusicK's loop header over them, END on the last block alone, its loop flag as asked and the loop point on its block.
    internal static byte[] FileBytesFromBlocks(byte[] blocks, int loopBlock, bool doesLoop)
    {
        if (blocks.Length == 0)
        {
            blocks = new byte[BlockSize];
        }

        int blockCount = blocks.Length / BlockSize;
        for (int block = 0; block < blockCount - 1; block++)
        {
            blocks[block * BlockSize] &= unchecked((byte)~EndFlag);
        }

        int lastHeader = (blockCount - 1) * BlockSize;
        MarkLastBlock(blocks, lastHeader, doesLoop);

        return WithLoopHeader(blocks, Math.Clamp(loopBlock, 0, blockCount - 1) * BlockSize);
    }

    // A block's header byte for a range and a filter, its two flags clear.
    public static byte HeaderByte(int shift, int filter)
    {
        return (byte)((shift << ShiftBit) | (filter << FilterBit));
    }

    // Writes a block's residuals into the bytes after its header, two to a byte with the earlier point in the high half.
    public static void WriteResiduals(byte[] blocks, int blockOffset, int[] residuals)
    {
        for (int point = 0; point < PointsPerBlock; point += ResidualsPerByte)
        {
            int high = residuals[point] & ResidualMask;
            int low = residuals[point + 1] & ResidualMask;
            blocks[blockOffset + BlockHeaderSize + point / ResidualsPerByte] = (byte)((high << ResidualBits) | low);
        }
    }

    // One point of BRR decode: the residual scaled by the block's range plus what its filter predicts, clipped as the hardware clips - answering the
    // 16-bit output point and carrying the history forward.
    public static short DecodePoint(int residual, int shift, int filter, ref BrrHistory history)
    {
        int scaled = shift <= MaxShift ? (residual << shift) >> 1 : DegenerateRangePoint(residual);
        int predicted = DspPoint.Clipped(scaled + FilterPrediction(filter, history));
        int keptFifteenBitPoint = (short)(predicted << 1) >> 1;

        history = history.After(keptFifteenBitPoint);
        return (short)DspPoint.Clipped(keptFifteenBitPoint * 2);
    }

    // What a block's filter predicts for the next point from the history, filter 0 predicting nothing.
    public static int FilterPrediction(int filter, BrrHistory history)
    {
        int previousPoint = history.PreviousPoint;
        int pointBeforePrevious = history.PointBeforePrevious;
        return filter switch
        {
            1 => previousPoint + ((-previousPoint) >> 4),
            2 => (previousPoint << 1) + ((-((previousPoint << 1) + previousPoint)) >> 5) - pointBeforePrevious + (pointBeforePrevious >> 4),
            3 => (previousPoint << 1) + ((-(previousPoint * 13)) >> 6) - pointBeforePrevious + ((pointBeforePrevious * 3) >> 4),
            _ => 0,
        };
    }

    // The range a block's header byte names.
    private static int ShiftOf(byte header)
    {
        return header >> ShiftBit;
    }

    // The filter a block's header byte names.
    private static int FilterOf(byte header)
    {
        return (header >> FilterBit) & FilterMask;
    }

    // One point's residual out of a block, as the signed four-bit value it is.
    private static int ResidualAt(byte[] fileBytes, int blockOffset, int point)
    {
        byte packed = fileBytes[blockOffset + BlockHeaderSize + point / ResidualsPerByte];
        int residual = point % ResidualsPerByte == 0 ? packed >> ResidualBits : packed & ResidualMask;
        return residual > MaxResidual ? residual - ResidualRange : residual;
    }

    // What a range above the largest the DSP shifts as written puts in front of the prediction.
    private static int DegenerateRangePoint(int residual)
    {
        return residual < 0 ? DegenerateNegativePoint : 0;
    }

    // The loop header's byte offset into the block data.
    private static int LoopOffsetOf(byte[] fileBytes)
    {
        return fileBytes[0] | (fileBytes[1] << ByteUnits.BitsPerByte);
    }

    // Block data under a loop header holding a byte offset, little-endian.
    internal static byte[] WithLoopHeader(ReadOnlySpan<byte> blocks, int loopOffset)
    {
        var fileBytes = new byte[LoopHeaderSize + blocks.Length];
        fileBytes[0] = (byte)(loopOffset & byte.MaxValue);
        fileBytes[1] = (byte)(loopOffset >> ByteUnits.BitsPerByte);
        blocks.CopyTo(fileBytes.AsSpan(LoopHeaderSize));
        return fileBytes;
    }

    // Marks the block whose header byte is at an offset as the sample's last, with its loop flag as asked.
    private static void MarkLastBlock(byte[] blocks, int headerIndex, bool doesLoop)
    {
        blocks[headerIndex] |= EndFlag;
        SetLoopFlag(blocks, headerIndex, doesLoop);
    }

    // Sets or clears the loop flag in one block's header byte.
    private static void SetLoopFlag(byte[] bytes, int headerIndex, bool doesLoop)
    {
        if (doesLoop)
        {
            bytes[headerIndex] |= LoopFlag;
            return;
        }

        bytes[headerIndex] &= unchecked((byte)~LoopFlag);
    }
}
