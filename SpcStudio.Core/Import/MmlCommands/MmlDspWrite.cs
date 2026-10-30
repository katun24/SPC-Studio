using SpcStudio.Model;

namespace SpcStudio.Import;

// A $F6, the raw DSP write: the register address and the byte written to it, for the registers the song can hold and FLG.
public sealed record MmlDspWrite(int Tick, int Register, int Value, int Line) : MmlEvent(Tick, Line)
{
    // The event a $F6 to a register becomes, sorted by the register it reaches, or null where the import does not model that register.
    public static MmlEvent? EventFor(int tick, int register, int value, int line)
    {
        return SnesDspRegister.Classify(register) switch
        {
            DspRegisterKind.Status => new MmlIgnored(tick, IgnoredCommand.DspStatusRegister, line),
            DspRegisterKind.Unused => new MmlIgnored(tick, IgnoredCommand.DspUnusedRegister, line),
            DspRegisterKind.MasterVolume => MmlIgnored.ForMasterVolume(tick, value, line),
            DspRegisterKind.PitchModulation => MmlIgnored.ForPitchModulation(tick, value, line),
            DspRegisterKind.Other => null,
            _ => new MmlDspWrite(tick, register, value, line),
        };
    }
}
