using SpcStudio.Model;

namespace SpcStudio.Import;

// A command read correctly and modeled by nothing, carried only so that the binder can say so where dropping it can be heard, and for a hot patch the two of
// its switches the readers follow: whether the read-ahead looks inside loops, and whether a rest skips the key-off it makes when it is read.
public sealed record MmlIgnored(int Tick, IgnoredCommand Command, int Line, bool DoesReadIntoLoops = false) : MmlEvent(Tick, Line)
{
    private const int ReadAheadIntoLoopsBit = 0x04;
    private const int FollowedFirstByteBits = ReadAheadIntoLoopsBit | AmkCommands.HotPatchContinuationBit;
    private const int RestKeyOffSkippedBit = 0x01;
    private const int ModulatedChannels = 0xFE;

    // Each preset's first two bytes of bits, as the driver's preset table holds them.
    private static readonly (int FirstByte, int SecondByte)[] PresetBits = [(0x80, 0x00), (0xFF, 0x00), (0x80, 0x00), (0x80, 0x01), (0x80, 0x01), (0x88, 0x00), (0x80, 0x01), (0x80, 0x01)];

    // Whether a rest skips the key-off it makes when it is read, from this hot patch on.
    public bool DoesRestSkipKeyOff { get; init; }

    // Whether this hot patch is the preset AddmusicK writes in front of an AddMusic4 song rather than one the song writes itself.
    public bool IsAddMusicFourPreset { get; init; }

    // Whether this is a $FA $7F or a $FA $FE, which rewrites the driver's own code.
    public bool IsHotPatch
    {
        get
        {
            return Command is IgnoredCommand.HotPatchPreset or IgnoredCommand.HotPatchBits or IgnoredCommand.FollowedHotPatch;
        }
    }

    // A $FA $7F, read off its preset's bytes of bits, a preset past the driver's table being read as one sending the read-ahead inside loops that the
    // readers do not follow.
    public static MmlIgnored ForHotPatchPreset(int tick, int preset, int line)
    {
        if (preset >= PresetBits.Length)
        {
            return new(tick, IgnoredCommand.HotPatchPreset, line, true);
        }

        var (firstByte, secondByte) = PresetBits[preset];
        return ForHotPatchBytes(tick, IgnoredCommand.HotPatchPreset, firstByte, secondByte, line);
    }

    // A $FA $FE, read off its first two bytes of bits, the second read only where the first says another follows and zero otherwise.
    public static MmlIgnored ForHotPatchBits(int tick, int firstByte, Func<int> readSecondByte, int line)
    {
        int secondByte = (firstByte & AmkCommands.HotPatchContinuationBit) != 0 ? readSecondByte() : 0;
        return ForHotPatchBytes(tick, IgnoredCommand.HotPatchBits, firstByte, secondByte, line);
    }

    // A hot patch read off its first two bytes of bits: the two switches the readers follow, and the command telling whether it switches on anything else.
    private static MmlIgnored ForHotPatchBytes(int tick, IgnoredCommand command, int firstByte, int secondByte, int line)
    {
        bool isEverythingFollowed = (firstByte & ~FollowedFirstByteBits) == 0;
        return new(tick, isEverythingFollowed ? IgnoredCommand.FollowedHotPatch : command, line, (firstByte & ReadAheadIntoLoopsBit) != 0)
        {
            DoesRestSkipKeyOff = (secondByte & RestKeyOffSkippedBit) != 0,
        };
    }

    // A fade as written, or the zero-tick fade it is dropped as where its duration is no ticks.
    public static MmlEvent ZeroTickFadeOr(MmlEvent fade, int durationTicks)
    {
        if (durationTicks == 0)
        {
            return new MmlIgnored(fade.Tick, IgnoredCommand.ZeroTickFade, fade.Line);
        }

        return fade;
    }

    // A $FA $00, read off its mask: heard where it sets a bit the DSP modulates, inert where it sets none.
    public static MmlIgnored ForPitchModulation(int tick, int mask, int line)
    {
        var command = (mask & ModulatedChannels) != 0 ? IgnoredCommand.PitchModulation : IgnoredCommand.InertPitchModulation;
        return new(tick, command, line);
    }

    // A $F6 to a master volume, read off its value: heard where it writes anything but the song load's own value, inert where it restates that.
    public static MmlIgnored ForMasterVolume(int tick, int value, int line)
    {
        var command = value == SnesDspRegister.DefaultMasterVolume ? IgnoredCommand.InertMasterVolume : IgnoredCommand.MasterVolume;
        return new(tick, command, line);
    }
}
