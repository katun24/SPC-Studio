namespace SpcStudio.Import;

// A remote gain: the one thing a remote code call can arm that is no block at all, a GAIN byte written to the voice at the ticks the trigger fires at.
public sealed record MmlRemoteGain(int Tick, int Gain, int Line) : MmlEvent(Tick, Line)
{
    public const int Adsr1 = 0x7F;
    public const int AddressPage = 0x01;
    private const int BlockLabelBase = -0x100;

    // The remote block label a remote gain of this byte is filed under, below every label a song or an address can name.
    public static int BlockLabelOf(int gain)
    {
        return BlockLabelBase - gain;
    }
}
