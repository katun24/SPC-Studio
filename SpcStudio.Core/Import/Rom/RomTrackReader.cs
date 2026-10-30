using SpcStudio.Model;

namespace SpcStudio.Import;

// Turns one song's compiled ARAM image back into what the text parser makes of a file.
public static class RomTrackReader
{
    internal const int Tie = 0xC6;
    internal const int FirstPercussion = 0xD0;
    internal const int TrackEnd = 0x00;
    internal const int FirstNoteByte = 0x80;
    internal const int NoTextLine = 0;
    private const int ArpeggioSpecialFormLength = 4;
    private const int ArpeggioHeaderBytes = 3;

    // Makes the name this reader gives a sample number.
    public static string SampleName(int number)
    {
        return $"rom-{number:X2}.brr";
    }

    // Reads one song out of a ROM under whichever read-ahead its hot patches leave the driver with, refusing a song that uses something the model has no home for.
    public static MmlFile Read(RomSong song, IReadOnlyList<byte>? echoFilters)
    {
        return ReadAheadPatch.ReadUnderPatch(doesReadIntoLoops => new RomSongReader(song, echoFilters, doesReadIntoLoops).ReadSong());
    }

    // Measures the length of the command at an offset into a song's bytes, the two of variable length included.
    internal static int CommandLength(byte[] data, int offset)
    {
        byte command = data[offset];
        if (command == AmkCommands.Parameter && offset + 1 < data.Length && data[offset + 1] == AmkCommands.HotPatchBitsSubcommand)
        {
            int length = 2;
            while (offset + length < data.Length && (data[offset + length] & AmkCommands.HotPatchContinuationBit) != 0)
            {
                length++;
            }

            return length + 1;
        }

        if (command == AmkCommands.Arpeggio)
        {
            return offset + 1 < data.Length && data[offset + 1] < MmlArpeggio.FirstSpecialForm ? data[offset + 1] + ArpeggioHeaderBytes : ArpeggioSpecialFormLength;
        }

        return AmkCommands.LengthOf(command);
    }
}
