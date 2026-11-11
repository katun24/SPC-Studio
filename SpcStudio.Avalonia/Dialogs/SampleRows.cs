using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The rows of a drop-down of a song's samples: the song's own, then a divider and the sample group's samples the song offers.
public static class SampleRows
{
    // The rows, the ones given standing last under a divider of their own.
    public static List<object> Of(Song song, IEnumerable<DropDownRow> trailingRows)
    {
        return DividedGroups.Join<object>([song.Samples.Select(RowOf), song.PickableSmwSamples.Select(RowOf), trailingRows], () => UnpickableRows.Divider());
    }

    // Where the row of a sample id stands among rows, or -1 where none does.
    public static int IndexOf(List<object> rows, int sampleId)
    {
        return rows.FindIndex(row => row is DropDownRow sampleRow && sampleRow.Id == sampleId);
    }

    // The row a sample reads as.
    private static DropDownRow RowOf(Sample sample)
    {
        return new DropDownRow(sample.Id, sample.Name);
    }
}
