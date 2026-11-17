namespace SpcStudio.Model;

// The GAIN byte's own layout over a raw byte: where the mode and the value sit in it, and how far the value can go in each mode.
public static class GainByte
{
    public const int MaxDirectLevel = 127;
    public const int MaxRate = 31;
    private const int SlopedModeBit = 0x80;
    private const int SlopedModeShift = 5;
    private const int SlopedModeMask = 3;

    // The mode a GAIN byte is in.
    public static GainMode ModeOf(int gain)
    {
        if ((gain & SlopedModeBit) == 0)
        {
            return GainMode.Direct;
        }

        return (GainMode)(((gain >> SlopedModeShift) & SlopedModeMask) + 1);
    }

    // The level a GAIN byte holds in Direct mode, or the rate it holds in the four sloped ones.
    public static int ValueOf(int gain)
    {
        return gain & MaxValueOf(ModeOf(gain));
    }

    // The largest value a mode can hold.
    public static int MaxValueOf(GainMode mode)
    {
        return mode == GainMode.Direct ? MaxDirectLevel : MaxRate;
    }

    // The GAIN byte for a mode and a value, the value clamped to what the mode can hold.
    public static byte Compose(GainMode mode, int value)
    {
        if (mode == GainMode.Direct)
        {
            return (byte)Math.Clamp(value, 0, MaxDirectLevel);
        }

        return (byte)(SlopedModeBit | ((((int)mode - 1) & SlopedModeMask) << SlopedModeShift) | Math.Clamp(value, 0, MaxRate));
    }
}
