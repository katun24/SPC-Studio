namespace SpcStudio.Import;

// A ROM that cannot be read at all: the wrong kind of file, or one whose AddmusicK tables are damaged, each made with the message that says which.
public sealed class RomReadException : Exception
{
    private RomReadException(string message) : base(message)
    {
    }

    // A file too small to hold an SNES ROM.
    public static RomReadException FileTooSmall()
    {
        return new RomReadException("The selected file is not a valid SNES ROM.");
    }

    // A ROM that AddmusicK has not patched.
    public static RomReadException NoAddmusicKData()
    {
        return new RomReadException("The ROM has no AddmusicK songs in it.");
    }

    // A sample whose bytes run past the end of the ROM.
    public static RomReadException SamplePastRomEnd(int sampleNumber)
    {
        return new RomReadException($"Sample {sampleNumber} runs past the end of the ROM.");
    }

    // A song whose bytes run past the end of the ROM.
    public static RomReadException SongPastRomEnd(int songSlot)
    {
        return new RomReadException($"Song ${songSlot:X2} runs past the end of the ROM.");
    }

    // A pointer table that runs past the end of the ROM.
    public static RomReadException PointerTablePastRomEnd()
    {
        return new RomReadException("A pointer table runs past the end of the ROM.");
    }
}
