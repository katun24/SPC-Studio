namespace SpcStudio.Import;

// One .spc, held whole: its tags' text, the file's own bytes, and its ARAM and DSP registers as views over those bytes.
public sealed record SpcFile(string Title, string Game, string Artist, string Comment, byte[] FileBytes)
{
    // The SPC700's ARAM, as a view over the file's bytes.
    public ReadOnlySpan<byte> Aram
    {
        get
        {
            return SpcReader.AramOf(FileBytes);
        }
    }

    // The DSP's registers as they stood when the dump was taken.
    public ReadOnlySpan<byte> DspRegisters
    {
        get
        {
            return SpcReader.DspRegistersOf(FileBytes);
        }
    }
}
