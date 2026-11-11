using System.Buffers.Binary;
using SpcStudio.Model;

namespace SpcStudio.Import;

// The music in an AddmusicK-patched ROM, read out of the tables AddmusicK's own patch leaves behind: the local songs, the sample table and the driver's echo filter tables, as bytes.
public sealed class RomMusic
{
    private const string AddmusicKTag = "@AMK";
    private const int HeaderOffset = 0x70000;
    private const int HeaderMinimumBytes = 0x10;
    private const int SampleGroupTableField = 5;
    private const int PointerTableField = 8;
    private const int PointerTerminator = 0xFFFFFF;
    private const int PointerBytes = 3;
    private const int CopierHeaderBytes = 512;
    private const int BankBytes = 0x8000;
    private const int MapperOffset = 0x7FD5;
    private const int Sa1Mapper = 0x23;
    private const int Sa1HighBankStart = 0x808000;
    private const int Sa1HighBankShift = 0x400000;
    private const int EngineAddress = 0x0F8000;
    private const int BankAddressMask = 0xFF0000;
    private const int UploadHeaderBytes = 4;
    private const int SampleLengthBytes = 2;

    // A ROM's music, as read.
    private RomMusic(IReadOnlyList<RomSong> songs, IReadOnlyList<RomSample?> samples, IReadOnlyList<byte>? echoFilters)
    {
        Songs = songs;
        Samples = samples;
        EchoFilters = echoFilters;
    }

    // The local songs the ROM holds, in slot order.
    public IReadOnlyList<RomSong> Songs { get; }

    // Every sample slot the ROM's table has, by number, null where the slot is empty.
    public IReadOnlyList<RomSample?> Samples { get; }

    // The bytes the driver holds from its first echo filter table on, every byte a $F1 filter index can reach, or null where the tables were not found.
    public IReadOnlyList<byte>? EchoFilters { get; }

    // Reads a ROM file's bytes, refusing a file that is not an AddmusicK-patched ROM.
    public static RomMusic Read(byte[] file)
    {
        byte[] romWithoutCopierHeader = file.Length % BankBytes == CopierHeaderBytes ? file[CopierHeaderBytes..] : file;
        if (romWithoutCopierHeader.Length <= HeaderOffset + HeaderMinimumBytes)
        {
            throw RomReadException.FileTooSmall();
        }

        bool doesUseSa1 = romWithoutCopierHeader[MapperOffset] == Sa1Mapper;
        if (System.Text.Encoding.ASCII.GetString(romWithoutCopierHeader, HeaderOffset, AddmusicKTag.Length) != AddmusicKTag)
        {
            throw RomReadException.NoAddmusicKData();
        }

        // A SNES address as an offset into the ROM file, under the ROM's own mapper.
        int ToFileOffset(int address)
        {
            if (doesUseSa1 && address >= Sa1HighBankStart)
            {
                address -= Sa1HighBankShift;
            }

            return ((address & 0x7F0000) >> 1) | (address & 0x7FFF);
        }

        int sampleGroupTable = ReadLong(romWithoutCopierHeader, HeaderOffset + SampleGroupTableField);
        int pointerTable = ReadLong(romWithoutCopierHeader, HeaderOffset + PointerTableField);
        int tableOffset = ToFileOffset(pointerTable);
        List<int> songPointers = ReadPointers(romWithoutCopierHeader, ref tableOffset);
        List<int> samplePointers = ReadPointers(romWithoutCopierHeader, ref tableOffset);
        var samples = ReadSamples(romWithoutCopierHeader, samplePointers, tableOffset, ToFileOffset);
        var songs = ReadSongs(romWithoutCopierHeader, songPointers, sampleGroupTable, ToFileOffset);
        return new RomMusic(songs, samples, ReadEchoFilters(romWithoutCopierHeader, ToFileOffset(EngineAddress)));
    }

    // Every sample slot, each one's blocks joined to the loop offset the table after the pointers holds for it.
    private static List<RomSample?> ReadSamples(byte[] rom, List<int> samplePointers, int loopTableOffset, Func<int, int> toFileOffset)
    {
        var samples = new List<RomSample?>(samplePointers.Count);
        for (int number = 0; number < samplePointers.Count; number++)
        {
            int loopOffset = ReadWord(rom, loopTableOffset + number * 2);
            if (samplePointers[number] == 0)
            {
                samples.Add(null);
                continue;
            }

            int blockOffset = toFileOffset(samplePointers[number]);
            int dataBytes = ReadWord(rom, blockOffset);
            if (blockOffset + SampleLengthBytes + dataBytes > rom.Length)
            {
                throw RomReadException.SamplePastRomEnd(number);
            }

            byte[] brr = dataBytes == 0 ? [] : new byte[dataBytes + SampleLengthBytes];
            if (dataBytes > 0)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(brr, (ushort)loopOffset);
                Array.Copy(rom, blockOffset + SampleLengthBytes, brr, SampleLengthBytes, dataBytes);
            }

            samples.Add(new RomSample(number, brr));
        }

        return samples;
    }

    // Every local song, each with the sample list its slot in the sample group table points at.
    private static List<RomSong> ReadSongs(byte[] rom, List<int> songPointers, int sampleGroupTable, Func<int, int> toFileOffset)
    {
        int groupTableOffset = toFileOffset(sampleGroupTable);
        int sampleGroupBank = sampleGroupTable & BankAddressMask;
        var songs = new List<RomSong>();
        for (int slot = 0; slot < songPointers.Count; slot++)
        {
            if (songPointers[slot] == 0)
            {
                continue;
            }

            int blockOffset = toFileOffset(songPointers[slot]);
            int dataBytes = ReadWord(rom, blockOffset);
            int aramBase = ReadWord(rom, blockOffset + 2);
            if (blockOffset + UploadHeaderBytes + dataBytes > rom.Length)
            {
                throw RomReadException.SongPastRomEnd(slot);
            }

            var sampleNumbers = new List<int>();
            int listPointer = ReadWord(rom, groupTableOffset + slot * 2);
            if (listPointer != 0)
            {
                int listOffset = toFileOffset(sampleGroupBank | listPointer);
                for (int entry = 0; entry < rom[listOffset]; entry++)
                {
                    sampleNumbers.Add(ReadWord(rom, listOffset + 1 + entry * 2));
                }
            }

            int songDataOffset = blockOffset + UploadHeaderBytes;
            songs.Add(new RomSong(slot, aramBase, rom[songDataOffset..(songDataOffset + dataBytes)], sampleNumbers));
        }

        return songs;
    }

    // The driver's first echo filter table and the bytes behind it, found inside the SPC driver image, or null where they cannot be found exactly once with a whole window behind them.
    private static IReadOnlyList<byte>? ReadEchoFilters(byte[] rom, int engineOffset)
    {
        if (engineOffset < 0 || engineOffset + UploadHeaderBytes > rom.Length)
        {
            return null;
        }

        int imageBytes = ReadWord(rom, engineOffset);
        int driverImageStart = engineOffset + UploadHeaderBytes;
        if (imageBytes <= 0 || driverImageStart + imageBytes > rom.Length)
        {
            return null;
        }

        byte[] presetFilterTableBytes = [.. SnesEcho.PresetCoefficients.SelectMany(filter => filter).Select(coefficient => (byte)coefficient)];
        int foundOffset = -1;
        for (int offset = driverImageStart; offset + presetFilterTableBytes.Length <= driverImageStart + imageBytes; offset++)
        {
            if (!rom.AsSpan(offset, presetFilterTableBytes.Length).SequenceEqual(presetFilterTableBytes))
            {
                continue;
            }

            if (foundOffset >= 0)
            {
                return null;
            }

            foundOffset = offset;
        }

        if (foundOffset < 0 || foundOffset + SnesEcho.FilterWindowBytes > driverImageStart + imageBytes)
        {
            return null;
        }

        return rom[foundOffset..(foundOffset + SnesEcho.FilterWindowBytes)];
    }

    // One terminated run of 24-bit pointers, leaving the offset on the byte after the terminator.
    private static List<int> ReadPointers(byte[] rom, ref int offset)
    {
        var pointers = new List<int>();
        while (true)
        {
            if (offset + PointerBytes > rom.Length)
            {
                throw RomReadException.PointerTablePastRomEnd();
            }

            int pointer = ReadLong(rom, offset);
            offset += PointerBytes;
            if (pointer == PointerTerminator)
            {
                return pointers;
            }

            pointers.Add(pointer);
        }
    }

    // A little-endian 16-bit value.
    private static int ReadWord(byte[] rom, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(rom.AsSpan(offset));
    }

    // A little-endian 24-bit value.
    private static int ReadLong(byte[] rom, int offset)
    {
        return ReadWord(rom, offset) | rom[offset + 2] << 16;
    }
}
