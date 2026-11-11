namespace SpcStudio.Model;

// One stretch of the song played with the echo running: a start, a length, and the three settings the unit holds across the whole of it.
public readonly record struct EchoRun(int StartTick, int LengthTicks, EchoUnitState State) : ILaneUnit<EchoRun>
{
    // The same run over a different stretch, carrying the same settings.
    public EchoRun WithSpan(int startTick, int lengthTicks)
    {
        return this with { StartTick = startTick, LengthTicks = lengthTicks };
    }
}
