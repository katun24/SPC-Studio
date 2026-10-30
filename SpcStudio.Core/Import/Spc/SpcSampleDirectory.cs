using System.Buffers.Binary;
using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio.Import;

// The DSP's sample directory read out of an ARAM image, an entry at a time by its number.
public static class SpcSampleDirectory
{
    public const int MaxEntries = 256;
    public const int MaxBlocks = SpcReader.AramSize / BrrSample.BlockSize;
    private const int EntrySize = 4;
    private const int LoopAddressOffset = 2;

    // The entries of a dump's directory a list of numbers names, in the list's order, a number whose slot does not read as a sample left out.
    public static IReadOnlyList<SpcDirectoryEntry> EntriesOf(SpcFile file, IEnumerable<int> indexes)
    {
        var entries = new List<SpcDirectoryEntry>();
        foreach (int index in indexes)
        {
            if (EntryOf(file.Aram, file.DspRegisters, index) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    // One entry of the directory the DSP registers point at in an ARAM image by its number, wherever it sits in the table, or null where that slot does not read as a sample.
    public static SpcDirectoryEntry? EntryOf(ReadOnlySpan<byte> aram, ReadOnlySpan<byte> registers, int index)
    {
        return EntryAt(aram, registers[SnesDspRegister.SampleDirectory], index);
    }

    // One entry of the directory on an ARAM page, wherever it sits in the table, or null where that slot does not read as a sample.
    private static SpcDirectoryEntry? EntryAt(ReadOnlySpan<byte> aram, byte directoryPage, int index)
    {
        if (index < 0 || index >= MaxEntries)
        {
            return null;
        }

        int entryAddress = (directoryPage << ByteUnits.BitsPerByte) + index * EntrySize;
        if (entryAddress + EntrySize > aram.Length)
        {
            return null;
        }

        ushort startAddress = BinaryPrimitives.ReadUInt16LittleEndian(aram[entryAddress..]);
        ushort loopAddress = BinaryPrimitives.ReadUInt16LittleEndian(aram[(entryAddress + LoopAddressOffset)..]);
        if (!IsSample(aram, startAddress, loopAddress, out int blockCount, out bool doesLoop))
        {
            return null;
        }

        return new SpcDirectoryEntry(index, startAddress, blockCount, loopAddress - startAddress, doesLoop);
    }

    // Whether a start and a loop address read as a sample: the blocks walk to an END flag inside ARAM, and where the last block loops, the loop address is a block boundary inside them.
    private static bool IsSample(ReadOnlySpan<byte> aram, ushort startAddress, ushort loopAddress, out int blockCount, out bool doesLoop)
    {
        blockCount = 0;
        doesLoop = false;
        if (startAddress == 0)
        {
            return false;
        }

        int blockAddress = startAddress;
        while (blockCount < MaxBlocks)
        {
            if (blockAddress + BrrSample.BlockSize > aram.Length)
            {
                return false;
            }

            byte header = aram[blockAddress];
            blockCount++;
            blockAddress += BrrSample.BlockSize;
            if ((header & BrrSample.EndFlag) == 0)
            {
                continue;
            }

            doesLoop = (header & BrrSample.LoopFlag) != 0;
            if (!doesLoop)
            {
                return true;
            }

            return loopAddress >= startAddress && loopAddress < blockAddress && (loopAddress - startAddress) % BrrSample.BlockSize == 0;
        }

        return false;
    }
}
