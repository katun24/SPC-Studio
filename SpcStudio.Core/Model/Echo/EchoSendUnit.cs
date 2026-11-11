namespace SpcStudio.Model;

// One stretch of a channel fed into the echo buffer: its bit in $EF's mask set where the unit starts and cleared again where it ends.
public readonly record struct EchoSendUnit(int StartTick, int LengthTicks) : ILaneUnit<EchoSendUnit>
{
    // The same unit over a different stretch.
    public EchoSendUnit WithSpan(int startTick, int lengthTicks)
    {
        return new(startTick, lengthTicks);
    }
}
