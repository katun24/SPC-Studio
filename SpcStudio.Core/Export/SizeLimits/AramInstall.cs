using SpcStudio.Import;

namespace SpcStudio.Export;

// What an AddmusicK install says about ARAM: the room it leaves one local song, the sample groups it defines in its own order, and what each group loads.
public sealed record AramInstall(int AvailableBytes, IReadOnlyList<SampleGroup> SampleGroups, IReadOnlyDictionary<string, SampleGroupLoad> LoadBySampleGroup)
{
    // The largest echo buffer any of the install's global songs asks for, which AddmusicK gives every local song at least.
    public int GlobalSongsEchoBufferBytes { get; init; }
}
