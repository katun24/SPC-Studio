using System.Collections.Immutable;

namespace SpcStudio.Model;

// One row of a pan's rows as both saved files write them, a note's or an instrument's: the tick it takes over at from the note's start, the pan it sets,
// the surround pair its $DB carries and the pan it ramps to, if any.
internal sealed class PanRegionDto
{
    public int Offset { get; set; }
    public int Value { get; set; }
    public int Surround { get; set; }
    public int? FadeTo { get; set; }

    // Pan rows as they are written, or null where there are none.
    public static List<PanRegionDto>? ListOf(ImmutableArray<NotePanRegion> rows)
    {
        return SavedFileJson.EntriesOrNull(rows, row => new PanRegionDto
        {
            Offset = row.OffsetTicks,
            Value = row.State.Value,
            Surround = row.State.Surround.Packed,
            FadeTo = row.FadeTo,
        });
    }

    // Pan rows read back in file order, refusing a pan or a surround pair out of range.
    public static ImmutableArray<NotePanRegion> ChainOf(List<PanRegionDto>? rows, string where)
    {
        foreach (var row in rows ?? [])
        {
            SavedFileChecks.RequireSurround(where, row.Surround);
        }

        ImmutableArray<NotePanRegion> chain = [.. (rows ?? []).Select(row => NotePan.Lane.WithFadeTo(
            new NotePanRegion(row.Offset, new PanState(row.Value, SnesSurround.FromPacked(row.Surround))),
            row.FadeTo ?? row.Value))];
        SavedFileChecks.RequirePanChain(where, chain);
        return chain;
    }
}
