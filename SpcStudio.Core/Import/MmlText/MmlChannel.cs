namespace SpcStudio.Import;

// One channel's stream of events, the tick its clock finished at, and the facts about its compiled bytes that the events alone cannot say.
public sealed record MmlChannel(int Number, IReadOnlyList<MmlEvent> Events, int EndTick)
{
    // The ties read with a key-off in front of them - opening a pass of a loop body, straight after a loop, or opening a phrase on a ROM - which may be rests.
    public IReadOnlyList<MmlCutTie> CutTies { get; init; } = [];

    // What the read-ahead finds from the byte ending at each loop edge, where the stream's next byte is not the one played next.
    public IReadOnlyList<MmlLoopEdge> LoopEdges { get; init; } = [];

    // The ticks a rest byte starts at, where a rest the model holds as one stretch is split into the bytes the driver cuts in.
    public IReadOnlyList<int> RestBytes { get; init; } = [];

    // The ticks a tie byte with no note in front of it starts at, which the rest bytes leave out.
    public IReadOnlyList<int> RestTies { get; init; } = [];

    // The $DD commands written in a rest, in stream order, once they have been lifted off the events; empty straight out of a reader.
    public IReadOnlyList<MmlRestBend> RestBends { get; init; } = [];
}
