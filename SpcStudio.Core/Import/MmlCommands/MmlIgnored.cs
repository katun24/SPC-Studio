using SpcStudio.Model;

namespace SpcStudio.Import;

// A command read correctly and modeled by nothing, carried only so that the binder can say so where dropping it can be heard, and whether it is a hot patch that sends the read-ahead into loops.
public sealed record MmlIgnored(int Tick, IgnoredCommand Command, int Line, bool DoesReadIntoLoops = false) : MmlEvent(Tick, Line)
{
    private const int ReadAheadIntoLoopsBit = 0x04;
    private const int ReadAheadIntoLoopsPreset = 0x01;
    private const int LastHotPatchPreset = 0x07;
    private const int ModulatedChannels = 0xFE;

    // A $FA $7F or a $FA $FE, read off its first argument byte: the preset number for the one and the first byte of bits for the other.
    public static MmlIgnored ForHotPatch(int tick, IgnoredCommand command, int argument, int line)
    {
        if (command != IgnoredCommand.HotPatchBits)
        {
            return new(tick, command, line, argument is ReadAheadIntoLoopsPreset or > LastHotPatchPreset);
        }

        var bitsCommand = argument == ReadAheadIntoLoopsBit ? IgnoredCommand.ReadAheadHotPatch : command;
        return new(tick, bitsCommand, line, (argument & ReadAheadIntoLoopsBit) != 0);
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

    // A $FA $00, or a $F6 to PMON, read off its mask: heard where it sets a bit the DSP modulates, inert where it sets none.
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
