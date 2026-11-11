using SpcStudio.Model;

namespace SpcStudio.Audio;

// The chip's echo at a frame: whether it is written at all, which voices send into it, its two volumes, its delay, its feedback and its FIR taps packed as the model packs them.
public readonly record struct SpcTraceEcho(long Frame, bool IsEnabled, int Sends, int VolumeLeft, int VolumeRight, int Delay, int Feedback, long Coefficients)
{
    // The same echo with its frame left out, which is what two points of the path are told apart by.
    public SpcTraceEcho WithoutFrame
    {
        get
        {
            return this with { Frame = 0 };
        }
    }

    // The echo a register image holds, taken as standing at a frame.
    public static SpcTraceEcho Of(ReadOnlySpan<byte> registers, long frame)
    {
        long coefficients = 0;
        for (int tap = 0; tap < SnesEcho.TapCount; tap++)
        {
            coefficients = SnesEcho.WithCoefficient(coefficients, tap, registers[SnesDspRegister.AddressOf(tap, SnesDspRegister.EchoCoefficientColumn)]);
        }

        return new SpcTraceEcho(
            frame,
            (registers[SnesDspRegister.Flags] & SnesDspRegister.FlagsEchoWriteOff) == 0,
            registers[SnesDspRegister.EchoSends],
            registers[SnesDspRegister.EchoVolumeLeft],
            registers[SnesDspRegister.EchoVolumeRight],
            registers[SnesDspRegister.EchoDelay] & SnesEcho.MaxDelay,
            registers[SnesDspRegister.EchoFeedback],
            coefficients);
    }
}
