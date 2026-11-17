namespace SpcStudio.Controls;

// What any list of rows is editing on one subject - one note or an instrument: the room its chain has and whether that room is a length of its own.
public interface IRowHost
{
    // The ticks a chain has to fit inside, which is a note's own length where there is one.
    int CapacityTicks { get; }

    // Whether the subject has a length of its own, which a note has and an instrument does not, its rows running on to the end of every note playing it.
    bool HasOwnLength { get; }
}
