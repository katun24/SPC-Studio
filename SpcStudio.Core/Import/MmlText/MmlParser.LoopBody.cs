namespace SpcStudio.Import;

public static partial class MmlParser
{
    // A loop body once it has been read: its events with ticks counted from its start, its length, the note a following ^ would extend, the tie it opens on and the ticks and q of
    // its opening ties, its rest bytes and the ties behind them, its loop edges, and whether it is a subloop.
    private sealed record LoopBody(
        IReadOnlyList<MmlEvent> Events, int LengthTicks, int HeldNoteIndex, int OpeningTieLine, int OpeningTieTicks, int? OpeningTieQuantization,
        IReadOnlyList<int> RestBytes, IReadOnlyList<int> RestTies, IReadOnlyList<MmlLoopEdge> LoopEdges, bool IsSubloop)
    {
        // The tick one pass of the body starts on, counted from the first pass's start.
        public int PassStartTick(int firstPassStartTick, int pass)
        {
            return firstPassStartTick + pass * LengthTicks;
        }
    }
}
