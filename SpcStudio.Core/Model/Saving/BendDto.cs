using System.Collections.Immutable;

namespace SpcStudio.Model;

// One $DD as both saved files write it, a note's or an instrument's: where its ramp sits, and its semitones counted from where the bend in front of it left off.
internal sealed class BendDto : SweepSpanDto
{
    public int Offset { get; set; }

    // A pitch bend chain as it is written, or null where there is none.
    public static List<BendDto>? ListOf(ImmutableArray<PitchBend> chain)
    {
        return SavedFileJson.EntriesOrNull(chain, bend => WithSpanOf(new BendDto { Offset = bend.OffsetSemitones }, bend));
    }

    // A pitch bend chain read back in file order, refusing a delay or a ramp length out of range.
    public static ImmutableArray<PitchBend> ChainOf(List<BendDto>? bends, string where)
    {
        ImmutableArray<PitchBend> listedChain = [.. (bends ?? []).Select(bend => new PitchBend(bend.Delay, bend.Length, bend.Offset) { IsEndless = bend.IsEndless })];
        SavedFileChecks.RequireBendChain(where, listedChain);
        return listedChain;
    }
}
