using SpcStudio.Audio;

namespace SpcStudio.Import;

// Takes a sample out of an .spc's ARAM and hands it back as the bytes of an AddmusicK .brr.
public static class SpcExtract
{
    // A directory entry's blocks under AddmusicK's loop header, which holds no loop where the last block does not ask the DSP to loop.
    public static byte[] BrrFileBytes(ReadOnlySpan<byte> aram, SpcDirectoryEntry entry)
    {
        int headerLoopOffset = entry.DoesLoop ? entry.LoopOffset : 0;
        return BrrSample.WithLoopHeader(aram.Slice(entry.StartAddress, entry.BlockCount * BrrSample.BlockSize), headerLoopOffset);
    }
}
