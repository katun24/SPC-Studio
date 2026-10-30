using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Import;

// A note as written: the driver's own note value, its length with its ties joined on, the whole q byte, and what its compiled bytes say about it.
public sealed record MmlNote(int Tick, int NoteValue, int LengthTicks, int Quantization, int Line) : MmlEvent(Tick, Line)
{
    // The channel's fine tuning as this note keys on.
    public int FineTune { get; init; } = SnesPitch.MinFineTune;

    // The note's $DD chain in the order it was written, each delay counted from where the one before it ended.
    public ImmutableArray<MmlPitchBend> Bends { get; init; } = [];

    // The ticks each of the note's note and tie bytes covers, in order, adding up to its length while nothing has rewritten it.
    public ImmutableArray<int> Pieces { get; init; } = [];

    // The ticks each byte of the rest behind the note covers, where a bend of the note's is written in that rest; empty on every other note.
    public ImmutableArray<int> RestPieces { get; init; } = [];

    // Whether a $FC key-off remote block was armed when the note keyed off.
    public bool IsHeld { get; init; }

    // The ties written under a q other than the one in force in front of them, in the order they are written.
    public ImmutableArray<MmlTieGate> TieGates { get; init; } = [];

    // The tick the note's last byte ends on.
    public int EndTick
    {
        get
        {
            return Tick + LengthTicks;
        }
    }

    // Whether the byte layout still adds up to the note's length, which a rewrite that lengthens the note ends.
    public bool HasKnownLayout
    {
        get
        {
            return Pieces.Sum() == LengthTicks;
        }
    }

    // The ticks of the note's last byte, which both of its cuts are made in, or null where the byte layout no longer says.
    public int? LastPieceTicks
    {
        get
        {
            return PieceTicksEndingAt(LengthTicks);
        }
    }

    // The q the note's last byte was written under, which is the one both of its cuts are made by.
    public int LastByteQuantization
    {
        get
        {
            if (LastPieceTicks is not { } lastPieceTicks)
            {
                return Quantization;
            }

            return QuantizationAt(LengthTicks - lastPieceTicks);
        }
    }

    // The ticks of the note's byte that ends that many ticks into it, or null where no byte ends there or the layout no longer says.
    public int? PieceTicksEndingAt(int offsetTicks)
    {
        if (!HasKnownLayout)
        {
            return null;
        }

        int pieceEndTicks = 0;
        foreach (int pieceTicks in Pieces)
        {
            pieceEndTicks += pieceTicks;
            if (pieceEndTicks == offsetTicks)
            {
                return pieceTicks;
            }

            if (pieceEndTicks > offsetTicks)
            {
                return null;
            }
        }

        return null;
    }

    // The note rewritten so that it sounds for exactly that many ticks from its key-on, in the first of three shapes that says it: as written, written
    // longer at the full gate no further than the reach, or held.
    public MmlNote RewrittenToSound(int soundingTicks, int? reachTick, Func<int, int> waitTimeAt)
    {
        // The wait time at a tick counted from the note's own start.
        int WaitTimeIntoNote(int offsetTicks)
        {
            return waitTimeAt(Tick + offsetTicks);
        }

        if (SnesTiming.KeyOnTicks(LengthTicks, Note.GateOfQuantization(Quantization), null, WaitTimeIntoNote) == soundingTicks)
        {
            return this;
        }

        if (reachTick is { } reach && SnesTiming.PlainLength(soundingTicks, reach - Tick, WaitTimeIntoNote) is { } plainLengthTicks)
        {
            return this with
            {
                LengthTicks = plainLengthTicks,
                Quantization = Note.QuantizationOf(SnesTiming.MaxGate, Note.VelocityOfQuantization(Quantization)),
            };
        }

        return HeldFor(soundingTicks);
    }

    // The note held, sounding for exactly that many ticks.
    public MmlNote HeldFor(int soundingTicks)
    {
        return this with { LengthTicks = soundingTicks, IsHeld = true };
    }

    // The note with a bend added to its chain, armed at the piece starting at a tick.
    public MmlNote WithBendArmedAt(int pieceStartTick, int delayTicks, int lengthTicks, int targetNoteValue)
    {
        return this with { Bends = Bends.Add(new MmlPitchBend(pieceStartTick - Tick, delayTicks, lengthTicks, targetNoteValue)) };
    }

    // The note cut short to that many ticks, its byte layout cut with it.
    public MmlNote ShortenedTo(int lengthTicks)
    {
        return this with { LengthTicks = lengthTicks, Pieces = PiecesSplitAt(lengthTicks).Kept };
    }

    // The byte layout parted that many ticks into the note: the bytes in front, the one the tick lands inside cut to what lies in front of it, and the
    // bytes from there to the note's end, that one's remainder first.
    public (ImmutableArray<int> Kept, ImmutableArray<int> After) PiecesSplitAt(int offsetTicks)
    {
        var keptPieces = ImmutableArray.CreateBuilder<int>();
        var afterPieces = ImmutableArray.CreateBuilder<int>();
        int pieceStartTicks = 0;
        foreach (int pieceTicks in Pieces)
        {
            if (pieceStartTicks + pieceTicks <= offsetTicks)
            {
                keptPieces.Add(pieceTicks);
            }
            else if (pieceStartTicks >= offsetTicks)
            {
                afterPieces.Add(pieceTicks);
            }
            else
            {
                keptPieces.Add(offsetTicks - pieceStartTicks);
                afterPieces.Add(pieceStartTicks + pieceTicks - offsetTicks);
            }

            pieceStartTicks += pieceTicks;
        }

        return (keptPieces.ToImmutable(), afterPieces.ToImmutable());
    }

    // The ticks of the note's bytes from one byte boundary to another, both counted from its start, or null where either is not where a byte starts or the layout no longer says.
    public IReadOnlyList<int>? PiecesBetween(int fromOffsetTicks, int toOffsetTicks)
    {
        if (!HasKnownLayout || !IsByteBoundary(fromOffsetTicks) || !IsByteBoundary(toOffsetTicks))
        {
            return null;
        }

        List<int> pieces = [];
        int pieceStartTicks = 0;
        foreach (int pieceTicks in Pieces)
        {
            if (pieceStartTicks >= fromOffsetTicks && pieceStartTicks + pieceTicks <= toOffsetTicks)
            {
                pieces.Add(pieceTicks);
            }

            pieceStartTicks += pieceTicks;
        }

        return pieces;
    }

    // Whether a byte of the note starts or ends that many ticks into it.
    private bool IsByteBoundary(int offsetTicks)
    {
        return offsetTicks == 0 || PieceTicksEndingAt(offsetTicks) is not null;
    }

    // The q the note's byte starting that many ticks into it was written under.
    public int QuantizationAt(int offsetTicks)
    {
        int quantization = Quantization;
        foreach (var tieGate in TieGates)
        {
            if (tieGate.OffsetTicks > offsetTicks)
            {
                break;
            }

            quantization = tieGate.Quantization;
        }

        return quantization;
    }

    // The note's tie gates with one more for a tie starting at its end, where that tie's q is not the one in force there.
    public ImmutableArray<MmlTieGate> TieGatesWithTie(int tieQuantization)
    {
        if (QuantizationAt(LengthTicks) == tieQuantization)
        {
            return TieGates;
        }

        return TieGates.Add(new MmlTieGate(LengthTicks, tieQuantization));
    }

    // The ticks the note sounds for from its key-on, its last byte cut by that gate.
    public int SoundedTicks(int gate, int? finalPieceTicks, Func<int, int> waitTimeAt)
    {
        if (IsHeld)
        {
            return LengthTicks;
        }

        return SnesTiming.KeyOnTicks(LengthTicks, gate, finalPieceTicks, offsetTicks => waitTimeAt(Tick + offsetTicks));
    }
}
