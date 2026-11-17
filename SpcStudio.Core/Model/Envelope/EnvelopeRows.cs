using System.Collections.Immutable;

namespace SpcStudio.Model;

// The envelope rows an instrument holds: the first the envelope a note keys on under, each later one taking over where the one before it ends.
public static class EnvelopeRows
{
    // The rows a note standing on an instrument's rung plays before they are cut to its length: the instrument's own bytes at key-on, then its changes.
    public static ImmutableArray<EnvelopeRow> OfInstrument(Instrument instrument)
    {
        return [new EnvelopeRow(OffsetRows.FirstOffsetTicks, instrument.Envelope), .. instrument.EnvelopeChanges];
    }
}
