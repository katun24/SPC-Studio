using System.IO;
using SpcStudio.Model;

namespace SpcStudio.Audio;

// A decoded BRR sample - its points, where it loops back to and whether it does - and the one copy of the DSP's arithmetic over a .brr's bytes: the
// loop header, a block's header byte, its residuals and the point each residual decodes to.
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

    // The same bytes with the final block's loop flag set or cleared.
    public static byte[] WithLoopFlag(byte[] fileBytes, bool doesLoop, string name = DefaultName)
    {
        _ = HeaderSize(fileBytes, name);

        var edited = (byte[])fileBytes.Clone();
        SetLoopFlag(edited, fileBytes.Length - BlockSize, doesLoop);
        return edited;
    }

    // The same audio with the loop point moved to the start of a block, counted from 0 and clamped into the sample, under AddmusicK's loop header.
    public static byte[] WithLoopBlock(byte[] fileBytes, int block, string name = DefaultName)
    {
        int headerSize = HeaderSize(fileBytes, name);
        int blockCount = (fileBytes.Length - headerSize) / BlockSize;
        return WithLoopHeader(fileBytes.AsSpan(headerSize), Math.Clamp(block, 0, blockCount - 1) * BlockSize);
    }

    // An empty sample: AddmusicK's loop header over one silent block carrying END.
    public static byte[] SilentFileBytes()
    {
        return FileBytesFromBlocks([], 0, doesLoop: false);
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
        blocks[lastHeader] |= EndFlag;
        SetLoopFlag(blocks, lastHeader, doesLoop);

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
