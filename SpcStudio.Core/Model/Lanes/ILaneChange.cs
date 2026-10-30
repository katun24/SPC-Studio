namespace SpcStudio.Model;

// What a lane of changes needs of the thing on it: the tick it sits at.
public interface ILaneChange
{
    public int Tick { get; }
}
