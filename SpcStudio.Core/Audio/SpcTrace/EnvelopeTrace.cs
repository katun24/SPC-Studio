namespace SpcStudio.Audio;

// An envelope's shape from key-on: its corners in order, whether it settled at a level it would hold until key-off, and the corner each register change took over at, the key-on's first.
public readonly record struct EnvelopeTrace(IReadOnlyList<EnvelopeCorner> Corners, bool IsSettled, IReadOnlyList<int> HandoverCornerIndices)
{
    // The last corner the trace reached.
    public EnvelopeCorner LastCorner
    {
        get
        {
            return Corners[^1];
        }
    }

    // The corners one register change ran over: from the one it took over at up to the next change's, or to the end; none where the trace ended before it took over.
    public (int Start, int End) RunOf(int changeIndex)
    {
        if (changeIndex >= HandoverCornerIndices.Count)
        {
            return (Corners.Count, Corners.Count);
        }

        int end = changeIndex + 1 < HandoverCornerIndices.Count ? HandoverCornerIndices[changeIndex + 1] : Corners.Count;
        return (HandoverCornerIndices[changeIndex], end);
    }

    // The corner a phase was entered at while one register change ran, or null where it was not entered then.
    public EnvelopeCorner? CornerEntering(EnvelopePhase phase, int changeIndex)
    {
        var (start, end) = RunOf(changeIndex);
        for (int cornerIndex = start + 1; cornerIndex < end; cornerIndex++)
        {
            if (Corners[cornerIndex].Phase == phase && Corners[cornerIndex - 1].Phase != phase)
            {
                return Corners[cornerIndex];
            }
        }

        return null;
    }
}
