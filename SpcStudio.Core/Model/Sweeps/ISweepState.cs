namespace SpcStudio.Model;

// What a sweep map and the export need of a sweep's whole driver state: the state with no sweep, whether a state sweeps, the state as a voice can be
// heard playing it, and the state with its delay byte replaced.
public interface ISweepState<TSelf> : IEquatable<TSelf> where TSelf : struct, ISweepState<TSelf>
{
    // A channel with no sweep, which is what the song init leaves.
    public static abstract TSelf Off { get; }

    // Whether this state sweeps at all.
    public bool IsActive { get; }

    // This state as a voice can be heard playing it: itself where it sweeps, and no sweep where it does not.
    public TSelf Audible();

    // This state with its delay byte replaced and its other values untouched.
    public TSelf WithDelay(int delay);
}
