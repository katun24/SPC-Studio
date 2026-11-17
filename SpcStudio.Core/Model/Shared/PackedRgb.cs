namespace SpcStudio.Model;

// A color packed as red, green and blue bytes in one number, 0xRRGGBB, the way an instrument stores it.
public static class PackedRgb
{
    private const int RedShift = 16;
    private const int GreenShift = 8;

    // Packs three color bytes into one number.
    public static int Pack(byte red, byte green, byte blue)
    {
        return (red << RedShift) | (green << GreenShift) | blue;
    }

    // The red byte of a packed color.
    public static byte RedOf(int packedRgb)
    {
        return (byte)(packedRgb >> RedShift);
    }

    // The green byte of a packed color.
    public static byte GreenOf(int packedRgb)
    {
        return (byte)(packedRgb >> GreenShift);
    }

    // The blue byte of a packed color.
    public static byte BlueOf(int packedRgb)
    {
        return (byte)packedRgb;
    }
}
