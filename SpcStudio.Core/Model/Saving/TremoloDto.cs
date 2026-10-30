using System.Collections.Immutable;

namespace SpcStudio.Model;

// One stretch played under $E5 as both saved files write it, a note's or an instrument's: the sweep's span and rate, and its amount.
internal sealed class TremoloDto : SweepDto
{
    public int Amount { get; set; }

    // A tremolo chain as it is written, or null where there is none.
    public static List<TremoloDto>? ListOf(ImmutableArray<NoteTremolo> chain)
    {
        return SavedFileJson.EntriesOrNull(chain, tremolo => WithSpanOf(new TremoloDto { Rate = tremolo.Rate, Amount = tremolo.Amount }, tremolo));
    }

    // A tremolo chain read back in file order, refusing one that holds a value out of range.
    public static ImmutableArray<NoteTremolo> ChainOf(List<TremoloDto>? tremolos, string where)
    {
        // The chain as the file holds it.
        ImmutableArray<NoteTremolo> chain = [.. (tremolos ?? []).Select(tremolo => new NoteTremolo(tremolo.Delay, tremolo.Length, tremolo.Rate, tremolo.Amount) { IsEndless = tremolo.IsEndless })];
        SavedFileChecks.RequireTremoloChain(where, chain);
        return chain;
    }

    // A note's tremolo chain read back, refusing as an instrument's is refused and also one that runs past the note's length.
    public static ImmutableArray<NoteTremolo> NoteChainOf(List<TremoloDto>? tremolos, string where, int noteLengthTicks)
    {
        // The chain read back and checked as an instrument's is.
        var chain = ChainOf(tremolos, where);
        SavedFileChecks.RequireTremoloChainFitsNote(where, chain, noteLengthTicks);
        return chain;
    }
}
