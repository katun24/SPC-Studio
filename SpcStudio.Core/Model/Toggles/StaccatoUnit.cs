namespace SpcStudio.Model;

// One stretch of the song played under light staccato: $F4 $02 turned on where the unit starts and off again where it ends.
public readonly record struct StaccatoUnit(int StartTick, int LengthTicks) : ILaneUnit<StaccatoUnit>
{
    // The same unit over a different stretch.
    public StaccatoUnit WithSpan(int startTick, int lengthTicks)
    {
        return new(startTick, lengthTicks);
    }
}
