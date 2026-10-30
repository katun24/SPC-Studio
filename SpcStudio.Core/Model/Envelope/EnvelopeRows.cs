using System.Collections.Immutable;

namespace SpcStudio.Model;

// A note's envelope rows: the first the envelope it keys on under, each later one taking over where the one before it ends - what survives the note being cut, and the rows an instrument holds.
public static class EnvelopeRows
{
    // The rows a note standing on an instrument's rung plays before they are cut to its length: the instrument's own bytes at key-on, then its changes.
    public static ImmutableArray<EnvelopeRow> OfInstrument(Instrument instrument)
    {
        return [new EnvelopeRow(OffsetRows.FirstOffsetTicks, instrument.Envelope), .. instrument.EnvelopeChanges];
    }

    // The rows rebased onto a note trimmed at its head: the row in force at the new start becomes the key-on row, and every row after it keeps its tick.
    public static ImmutableArray<EnvelopeRow> TrimHead(ImmutableArray<EnvelopeRow> rows, int trimmedTicks)
    {
        return OffsetRows.TrimHead(rows, trimmedTicks, inForceIndex => rows[inForceIndex]);
    }

    // The envelope in force a number of ticks after key-on.
    public static EnvelopeState StateAt(ImmutableArray<EnvelopeRow> rows, int offsetTicks)
    {
        return rows[OffsetRows.InForceIndexAt(rows, offsetTicks)].State;
    }

    // The rows a note plays, its own where it holds any and its instrument's key-on envelope alone where it holds none.
    public static ImmutableArray<EnvelopeRow> PlayedBy(Note note, Instrument instrument)
    {
        return note.Envelopes.IsEmpty ? [new EnvelopeRow(OffsetRows.FirstOffsetTicks, instrument.Envelope)] : note.Envelopes;
    }
}
