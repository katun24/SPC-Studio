using System.Collections.Immutable;

namespace SpcStudio.Model;

// One row of a channel volume's rows as both saved files write them, a note's or an instrument's: the tick it takes over at from the note's start, the
// volume it sets and the volume it ramps to, if any.
internal sealed class VolumeRegionDto
{
    public int Offset { get; set; }
    public int Value { get; set; }
    public int? FadeTo { get; set; }

    // Volume rows as they are written, or null where there are none.
    public static List<VolumeRegionDto>? ListOf(ImmutableArray<NoteVolumeRegion> rows)
    {
        return SavedFileJson.EntriesOrNull(rows, row => new VolumeRegionDto
        {
            Offset = row.OffsetTicks,
            Value = row.State,
            FadeTo = row.FadeTo,
        });
    }

    // Volume rows read back in file order, refusing a volume out of range.
    public static ImmutableArray<NoteVolumeRegion> ChainOf(List<VolumeRegionDto>? rows, string where)
    {
        // The rows as the file holds them, a fade target equal to the row's own value read as no fade.
        ImmutableArray<NoteVolumeRegion> chain = [.. (rows ?? []).Select(row => NoteVolume.Lane.WithFadeTo(new NoteVolumeRegion(row.Offset, row.Value), row.FadeTo ?? row.Value))];
        SavedFileChecks.RequireVolumeChain(where, chain);
        return chain;
    }
}
