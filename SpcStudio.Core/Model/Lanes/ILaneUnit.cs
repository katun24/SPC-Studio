namespace SpcStudio.Model;

// What a lane of units needs of the thing on it: where it starts, how long it is, and how to make the same unit somewhere else.
public interface ILaneUnit<out TSelf>
{
    public int StartTick { get; }
    public int LengthTicks { get; }

    // The same unit over a different stretch, carrying whatever else it holds.
    public TSelf WithSpan(int startTick, int lengthTicks);
}
