using SpcStudio.Audio;

namespace SpcStudio.Import;

// One entry of the DSP's sample directory as it stands in the ARAM being read: its place in the table, where its blocks start in ARAM, how many blocks
// it runs for, its loop as AddmusicK's .brr spells it, and whether its last block loops at all.
public sealed record SpcDirectoryEntry(int Index, ushort StartAddress, int BlockCount, int LoopOffset, bool DoesLoop)
{
    // How many points it decodes to.
    public int PointCount
    {
        get
        {
            return BlockCount * BrrSample.PointsPerBlock;
        }
    }
}
