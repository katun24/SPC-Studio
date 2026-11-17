namespace SpcStudio.Model;

// What a lane of changes needs of the thing on it: the tick it sits at.
public interface ILaneChange
{
    public int Tick { get; }
}

// A change that can be made again at another tick, carrying whatever else it holds.
public interface ILaneChange<out TSelf> : ILaneChange
{
    // The same change at another tick.
    public TSelf WithTick(int tick);
}
