using SpcStudio.Import;

namespace SpcStudio.Export;

// What an AddmusicK install says about ARAM: the room it leaves one local song, what the program under that song is made of, the sample groups it
// defines in its own order, and what each group loads.
public sealed record AramInstall(int AvailableBytes, AmkProgramParts ProgramParts, IReadOnlyList<SampleGroup> SampleGroups, IReadOnlyDictionary<string, SampleGroupLoad> LoadBySampleGroup)
{
    // The largest echo buffer any of the install's global songs asks for, which AddmusicK gives every local song at least.
    public int GlobalSongsEchoBufferBytes { get; init; }

    // Where in ARAM a local song's data starts, everything the install leaves no room for lying below it.
    public int SongStartAddress
    {
        get
        {
            return SpcReader.AramSize - AvailableBytes;
        }
    }

    // What is left of ARAM once a song taking the bytes given is placed, below zero where AddmusicK will refuse it.
    public int FreeBytesAfter(int songBytes)
    {
        return AvailableBytes - songBytes;
    }
}
