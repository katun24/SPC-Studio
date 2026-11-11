using System.Collections.Immutable;

namespace SpcStudio.Import;

public static partial class MmlParser
{
    // A loop body once it has been read: its events with ticks counted from its start, its length, the note a following ^ would extend, the tie it opens on and the bytes and q of
    // its opening ties, its rest bytes and the ties behind them, the ties listed in it whose key-off is still to be decided, its loop edges, and whether it is a subloop.
    private sealed record LoopBody(
        IReadOnlyList<MmlEvent> Events, int LengthTicks, int HeldNoteIndex, int OpeningTieLine, ImmutableArray<int> OpeningTiePieces, int? OpeningTieQuantization,
        IReadOnlyList<int> RestBytes, IReadOnlyList<int> RestTies, IReadOnlyList<MmlCutTie> CutTies, IReadOnlyList<MmlLoopEdge> LoopEdges, bool IsSubloop)
    {
        // A [ ] body with nothing in it, which is what a call to a body never read plays.
        public static readonly LoopBody Empty = new(
            Events: [], LengthTicks: 0, HeldNoteIndex: NoNote, OpeningTieLine: NoLine, OpeningTiePieces: [], OpeningTieQuantization: null, RestBytes: [], RestTies: [], CutTies: [],
            LoopEdges: [], IsSubloop: false);

        // The ticks the ties the body opens with cover.
        public int OpeningTieTicks
        {
            get
            {
                return OpeningTiePieces.Sum();
            }
        }

        // The tick one pass of the body starts on, counted from the first pass's start.
        public int PassStartTick(int firstPassStartTick, int pass)
        {
            return firstPassStartTick + pass * LengthTicks;
        }
    }
}
