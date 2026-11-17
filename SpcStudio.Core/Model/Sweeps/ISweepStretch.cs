namespace SpcStudio.Model;

// What a sweep map needs of one stretch of a note's sweep: where it sits, whether it sweeps anything at all, and the state it puts in the driver's bytes.
public interface ISweepStretch<TSelf, TState> : INoteSweep<TSelf>
    where TSelf : struct, ISweepStretch<TSelf, TState>
    where TState : struct, ISweepState<TState>
{
    public bool IsActive { get; }

    // The whole state this puts in the driver's bytes while it runs, with a delay byte of zero.
    public TState State { get; }
}
