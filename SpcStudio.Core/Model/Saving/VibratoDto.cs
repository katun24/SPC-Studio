using System.Collections.Immutable;

namespace SpcStudio.Model;

// One stretch played under p as both saved files write it, a note's or an instrument's: the sweep's span and rate, $DE's extent and $EA's fade.
internal sealed class VibratoDto : SweepDto
{
    public int Extent { get; set; }
    public int Fade { get; set; }

    // A vibrato chain as it is written, or null where there is none.
    public static List<VibratoDto>? ListOf(ImmutableArray<NoteVibrato> chain)
    {
        return SavedFileJson.EntriesOrNull(chain, vibrato => WithSpanOf(new VibratoDto { Rate = vibrato.Rate, Extent = vibrato.Extent, Fade = vibrato.FadeTicks }, vibrato));
    }

    // A vibrato chain read back in file order, refusing one that holds a value out of range or breaks a fade rule.
    public static ImmutableArray<NoteVibrato> ChainOf(List<VibratoDto>? vibratos, string where)
    {
        ImmutableArray<NoteVibrato> savedChain = [.. (vibratos ?? []).Select(vibrato =>
            new NoteVibrato(vibrato.Delay, vibrato.Length, vibrato.Rate, vibrato.Extent, vibrato.Fade) { IsEndless = vibrato.IsEndless })];
        SavedFileChecks.RequireVibratoChain(where, savedChain);
        return savedChain;
    }

    // A note's vibrato chain read back, refusing as an instrument's is refused and also one that runs past the note's length.
    public static ImmutableArray<NoteVibrato> NoteChainOf(List<VibratoDto>? vibratos, string where, int noteLengthTicks)
    {
        var chainCheckedAsInstruments = ChainOf(vibratos, where);
        SavedFileChecks.RequireVibratoChainFitsNote(where, chainCheckedAsInstruments, noteLengthTicks);
        return chainCheckedAsInstruments;
    }
}
