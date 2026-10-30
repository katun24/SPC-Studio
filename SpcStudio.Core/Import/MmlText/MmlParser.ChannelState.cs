using SpcStudio.Model;

namespace SpcStudio.Import;

public static partial class MmlParser
{
    // One channel as it is being read: where its clock has got to, what has been written into it, and which of those a ^ would extend.
    private sealed class ChannelState
    {
        public bool HasChannelDirective;
        public bool IsPitchSlidePending;
        public bool IsHeldNoteFromLoop;

        // The tick the channel's clock has got to.
        public int EndTick;

        // The index of the note a tie extends, a rest's marker where the channel is holding a rest, or none where it is holding neither.
        public int HeldNoteIndex = NoNote;

        // The last duration byte AddmusicK emits on this channel, which is the length an & gives its bend, or none before the channel's first note.
        public int LastDurationByte = NoDurationByte;

        // The tick the last duration byte's own piece begins on, which is where a bend written behind it is armed, or none.
        public int LastPieceStartTick = NoTick;

        // The tick the run of note and ties being merged into one duration byte began on, or none.
        public int MergedRunStartTick = NoTick;

        // The ticks the run of note and ties being merged into one duration byte has taken.
        public int MergedRunTicks;

        // Whether the character after the run's last piece is a ^, so the next tie carries the run on.
        public bool IsMergedRunOpen;

        // A $DD whose two counts have been read and whose target is still to come as a note letter.
        public (int Delay, int Length, int Line)? BendAwaitingTarget;

        // The q byte notes on this channel are written under, starting at the driver's default gate and velocity.
        public int Quantization = Note.QuantizationOf(SnesTiming.DefaultGate, SnesVolume.DefaultVelocity);

        // The semitones the built-in instrument in force takes off every note written on this channel.
        public int BuiltinTranspose;

        // The note value the next note on this channel plays whatever it is written as, armed by a percussion @, or none.
        public int ForcedNoteValue = MmlBuiltins.NoForcedNoteValue;

        // Whether the last cut tie is a tie after a loop still being written, so that a following ^ adds to its length.
        public bool IsLastCutTieOpen;

        // What the rest or tie byte still being built is merged into.
        public RestByteKind OpenRestByte;

        // Whether a loop call has been read since the channel's last note, rest or tie in the stream.
        public bool HasCallSinceByte;

        // Whether the $00 that ends a [ ] body may have keyed the voice off since the last note byte.
        public bool MayVoiceBeKeyedOff;

        // The line of a loop call with no note, rest or tie in its body that nothing has been played behind since, in a channel something was played in before it, or none.
        public int HangingCallLine = NoLine;

        // The line of the first tie read as a rest for want of any note in front of it that stands after this channel's /, or none.
        public int OpeningTieAfterLoopLine = NoLine;

        public List<MmlEvent> Events { get; } = [];

        // The ties read with a key-off in front of them so far, which may be rests.
        public List<MmlCutTie> CutTies { get; } = [];

        // What the read-ahead finds from the byte ending at each loop edge read so far.
        public List<MmlLoopEdge> LoopEdges { get; } = [];

        // The ticks the rest bytes read so far start at.
        public List<int> RestBytes { get; } = [];

        // The ticks the tie bytes with no note in front of them read so far start at.
        public List<int> RestTies { get; } = [];

        // The loop edges in the channel's own stream whose read-ahead is still waiting on the next note, rest or tie.
        public List<int> PendingLoopEdges { get; } = [];
    }
}
