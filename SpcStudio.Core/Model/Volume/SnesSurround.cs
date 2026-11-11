namespace SpcStudio.Model;

// Surround sound: whether each side of a voice plays phase-inverted, which is y's second and third arguments.
public readonly record struct SnesSurround(bool IsLeftInverted, bool IsRightInverted)
{
    public const int MinPacked = 0;
    public const int MaxPacked = LeftPackedBit | RightPackedBit;
    public const int PanByteLeftShift = 7;
    public const int PanByteRightShift = 6;
    private const int LeftPackedBit = 2;
    private const int RightPackedBit = 1;

    public static readonly ValueRange PackedRange = new(MinPacked, MaxPacked);

    // Neither side inverted, which is what the channel init writes.
    public static readonly SnesSurround None = default;

    // Whether either side is inverted, which is what makes the export write the arguments.
    public bool IsAnyInverted
    {
        get
        {
            return IsLeftInverted || IsRightInverted;
        }
    }

    // The pair as one small int.
    public int Packed
    {
        get
        {
            return (IsLeftInverted ? LeftPackedBit : 0) | (IsRightInverted ? RightPackedBit : 0);
        }
    }

    // The pair the top two bits of a $DB pan byte say.
    public static SnesSurround InPanByte(int panByte)
    {
        return new((panByte & (1 << PanByteLeftShift)) != 0, (panByte & (1 << PanByteRightShift)) != 0);
    }

    // The pair a packed int holds.
    public static SnesSurround FromPacked(int packed)
    {
        return new((packed & LeftPackedBit) != 0, (packed & RightPackedBit) != 0);
    }
}
