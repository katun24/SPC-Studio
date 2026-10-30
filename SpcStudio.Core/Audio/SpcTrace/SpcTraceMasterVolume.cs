using SpcStudio.Model;

namespace SpcStudio.Audio;

// The chip's two master volumes at a frame, each side signed.
public readonly record struct SpcTraceMasterVolume(long Frame, int Left, int Right)
{
    // The master volumes a register image holds, taken as standing at a frame.
    public static SpcTraceMasterVolume Of(ReadOnlySpan<byte> registers, long frame)
    {
        return new SpcTraceMasterVolume(frame, (sbyte)registers[SnesDspRegister.MasterVolumeLeft], (sbyte)registers[SnesDspRegister.MasterVolumeRight]);
    }
}
