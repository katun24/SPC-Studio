namespace SpcStudio.Import;

public static partial class MmlParser
{
    // A loop body while it is being read, a [ ] or a [[ ]].
    private sealed class OpenLoopBody(bool isSubloop, int startTick, int definedLabel, int openingLine, bool isOpenedByHex = false)
    {
        public int OpeningTieLine = NoLine;
        public int OpeningTieTicks;
        public int? OpeningTieQuantization;
        public bool HasFetchedByte;
        public int BuiltinTransposeAtStart;
        public int ForcedNoteValueAtStart;

        // The index of the note a following ^ would extend, a rest's marker where the body is holding a rest, or none.
        public int HeldNoteIndex = NoNote;

        // Whether the run of ties this body opens with is still open, before its first note or rest.
        public bool IsOpeningTieRunOpen;

        // Whether the last note, tie or rest this subloop fetched was a [ ] body's.
        public bool IsLastByteFromCall;

        // Whether this is a [[ ]] subloop rather than a [ ] body.
        public bool IsSubloop { get; } = isSubloop;

        // Whether this subloop was opened as $E6 $00 rather than as [[.
        public bool IsOpenedByHex { get; } = isOpenedByHex;

        // The channel tick the body began at, which its events are measured from.
        public int StartTick { get; } = startTick;

        // The (n) label this body defines, or none.
        public int DefinedLabel { get; } = definedLabel;

        // The line the body was opened on.
        public int OpeningLine { get; } = openingLine;

        public List<MmlEvent> Events { get; } = [];

        // The loop edges in this [ ] body's own byte stream still waiting on the next note, rest or tie.
        public List<int> PendingLoopEdges { get; } = [];
    }
}
