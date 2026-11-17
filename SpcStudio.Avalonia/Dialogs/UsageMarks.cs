using System.Collections;
using SpcStudio.Model;

namespace SpcStudio;

// Which of the song's instruments and samples any note uses, marked on the rows of the drop-downs that list them.
public static class UsageMarks
{
    // Whether any note in the song plays an instrument.
    public static bool IsInstrumentUsed(Song song, int instrumentId)
    {
        return song.NoteCountUsingInstrument(instrumentId) > 0;
    }

    // Marks each row of an instrument drop-down by whether any note plays its instrument, where there is a song.
    public static void MarkInstrumentRows(Song? song, IEnumerable rows)
    {
        MarkRows(song, rows, IsInstrumentUsed);
    }

    // Marks each row of a sample drop-down by whether any note plays an instrument built on its sample, where there is a song.
    public static void MarkSampleRows(Song? song, IEnumerable rows)
    {
        MarkRows(song, rows, IsSampleUsed);
    }

    // Whether any note in the song plays an instrument built on a sample.
    private static bool IsSampleUsed(Song song, int sampleId)
    {
        return song.NoteCountUsingSample(sampleId) > 0;
    }

    // Marks each row among some by whether the song uses what it stands for, the dividers passed over, where there is a song.
    private static void MarkRows(Song? song, IEnumerable rows, Func<Song, int, bool> isUsed)
    {
        if (song is null)
        {
            return;
        }

        foreach (var row in rows.OfType<DropDownRow>())
        {
            row.IsUnused = !isUsed(song, row.Id);
        }
    }
}
