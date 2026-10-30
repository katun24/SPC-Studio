namespace SpcStudio.Model;

// The driver's volume chain: the song volume, the channel volume, the note's velocity, the pan and the amplify byte turned into a voice's DSP volume
// registers, and a register pair read back as the nearest chain.
public static class SnesVolume
{
    public const int MinPanIndex = 0;
    public const int MaxPanIndex = 20;
    public const int PanIndexMask = 0x1F;
    public const int DefaultMasterVolume = 0xC0;
    public const int MinMasterVolume = 0;
    public const int MaxMasterVolume = 255;
    public const int DefaultChannelVolume = 0xFF;
    public const int MinChannelVolume = 0;
    public const int MaxChannelVolume = 255;
    public const int DefaultPanIndex = 0x0A;
    public const int DefaultAmplify = 0x00;
    public const int MinAmplify = 0;
    public const int MaxAmplify = 255;
    public const int MinVelocity = 0;
    public const int MaxVelocity = 15;
    public const int DefaultVelocity = MaxVelocity;
    private const int PanFractionBits = 8;

    public static readonly ValueRange MasterVolumeRange = new(MinMasterVolume, MaxMasterVolume);
    public static readonly ValueRange PanIndexRange = new(MinPanIndex, MaxPanIndex);
    public static readonly ValueRange ChannelVolumeRange = new(MinChannelVolume, MaxChannelVolume);
    public static readonly ValueRange AmplifyRange = new(MinAmplify, MaxAmplify);
    public static readonly ValueRange VelocityRange = new(MinVelocity, MaxVelocity);

    // The velocity bytes q's low nybble selects between on SMW's own row.
    private static readonly byte[] SmwVelocityRow = [0x08, 0x12, 0x1B, 0x24, 0x2C, 0x35, 0x3E, 0x47, 0x51, 0x5A, 0x62, 0x6B, 0x7D, 0x8F, 0xA1, 0xB3];

    // The velocity bytes q's low nybble selects between on the N-SPC row, which every exported song plays on.
    private static readonly byte[] NspcVelocityRow = [0x19, 0x33, 0x4C, 0x66, 0x72, 0x7F, 0x8C, 0x99, 0xA5, 0xB2, 0xBF, 0xCC, 0xD8, 0xE5, 0xF2, 0xFC];

    // How much of the level each pan position lets through to one side, from hard right to hard left.
    private static readonly byte[] PanTable = [0x00, 0x01, 0x03, 0x07, 0x0D, 0x15, 0x1E, 0x29, 0x34, 0x42, 0x51, 0x5E, 0x67, 0x6E, 0x73, 0x77, 0x7A, 0x7C, 0x7D, 0x7E, 0x7F];

    // The pan index a $DB pan byte holds below its surround bits.
    public static int PanIndexInPanByte(int panByte)
    {
        return panByte & PanIndexMask;
    }

    // The velocity byte q's low nybble selects, off SMW's row or the N-SPC row.
    public static int VelocityByteFor(int velocity, bool usesSmwRow)
    {
        return (usesSmwRow ? SmwVelocityRow : NspcVelocityRow)[velocity];
    }

    // A note's level, its velocity given as q's low nybble rather than as the byte it selects.
    public static VoiceLevel LevelForNote(
        int masterVolume,
        int channelVolume,
        int velocity,
        int panIndex,
        bool usesSmwRow,
        int amplify = DefaultAmplify,
        SnesSurround surround = default,
        int panFraction = 0)
    {
        return new(masterVolume, channelVolume, VelocityByteFor(velocity, usesSmwRow), panIndex, amplify, surround, panFraction);
    }

    // A voice's DSP volume registers, its arguments in the order the driver reads them and its velocity as the byte q selects.
    public static VoiceVolume RegistersFor(
        int masterVolume,
        int channelVolume,
        int velocity,
        int panIndex,
        int amplify = DefaultAmplify,
        SnesSurround surround = default,
        int panFraction = 0)
    {
        // The level both sides share: the channel volume times the velocity times the song volume, squared.
        int level = channelVolume * velocity >> ByteUnits.BitsPerByte;
        level = level * masterVolume >> ByteUnits.BitsPerByte;
        level = level * level >> ByteUnits.BitsPerByte;

        // The pan as the driver's 8.8 value.
        int pan = panIndex << PanFractionBits | panFraction;

        int left = Amplified(level * PanEntryAt(pan) >> ByteUnits.BitsPerByte, amplify);
        int right = Amplified(level * PanEntryAt((MaxPanIndex << PanFractionBits) - pan) >> ByteUnits.BitsPerByte, amplify);
        return new VoiceVolume(surround.IsLeftInverted ? Negated(left) : left, surround.IsRightInverted ? Negated(right) : right);
    }

    // The channel volume, pan and surround pair that come nearest to reproducing a VOL pair under a song volume and a velocity byte, the amplify left at its default.
    public static VoiceMix Decompose(VoiceVolume volume, int masterVolume, int velocity)
    {
        var surround = new SnesSurround(volume.Left < 0, volume.Right < 0);
        int left = Math.Abs(volume.Left);
        int right = Math.Abs(volume.Right);
        if (left == 0 && right == 0)
        {
            return new VoiceMix(MinChannelVolume, DefaultPanIndex, surround);
        }

        int closestChannelVolume = MinChannelVolume;
        int closestPanIndex = DefaultPanIndex;
        long closestDistance = long.MaxValue;
        for (int panIndex = MinPanIndex; panIndex <= MaxPanIndex; panIndex++)
        {
            for (int channelVolume = MinChannelVolume; channelVolume <= MaxChannelVolume; channelVolume++)
            {
                var registers = RegistersFor(masterVolume, channelVolume, velocity, panIndex);
                long distanceFromTargetPair = Squared(registers.Left - left) + Squared(registers.Right - right);
                if (distanceFromTargetPair >= closestDistance)
                {
                    continue;
                }

                closestDistance = distanceFromTargetPair;
                closestChannelVolume = channelVolume;
                closestPanIndex = panIndex;
                if (distanceFromTargetPair == 0)
                {
                    return new VoiceMix(closestChannelVolume, closestPanIndex, surround);
                }
            }
        }

        return new VoiceMix(closestChannelVolume, closestPanIndex, surround);
    }

    // One side's entry out of the pan table for an 8.8 pan: the entry the high byte names, moved towards the one above it by the low byte's fraction.
    private static int PanEntryAt(int pan)
    {
        int index = pan >> PanFractionBits;
        int fraction = pan & byte.MaxValue;
        int entry = PanTable[index];
        if (fraction == 0 || index >= MaxPanIndex)
        {
            return entry;
        }

        return entry + ((PanTable[index + 1] - entry) * fraction >> ByteUnits.BitsPerByte);
    }

    // The amplify byte applied to one side, as a fraction of the side added to it, wrapped into the signed register.
    private static int Amplified(int side, int amplify)
    {
        return (sbyte)(side + (side * amplify >> ByteUnits.BitsPerByte));
    }

    // One side's register phase-inverted, wrapped into the signed register.
    private static int Negated(int side)
    {
        return (sbyte)-side;
    }

    // A distance squared.
    private static long Squared(int distance)
    {
        return (long)distance * distance;
    }
}
