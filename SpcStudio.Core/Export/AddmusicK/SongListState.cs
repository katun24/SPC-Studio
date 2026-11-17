using SpcStudio.Model;

namespace SpcStudio.Export;

// The song list's two tables as one state an undo can put back: each table's rows in number order with no number skipped, the local table starting
// after the global one, and what each edit of them makes.
public sealed class SongListState : IUndoState<SongListState>
{
    // A state holding the two tables' rows as given.
    private SongListState(IReadOnlyList<SongListEntry> globalRows, IReadOnlyList<SongListEntry> localRows)
    {
        GlobalRows = globalRows;
        LocalRows = localRows;
    }

    // A state with both tables empty.
    public static SongListState Empty { get; } = new([], []);

    public IReadOnlyList<SongListEntry> GlobalRows { get; }
    public IReadOnlyList<SongListEntry> LocalRows { get; }

    // The number the global table's '+' adds: the one after its last row, or the first there is.
    public int NextGlobalNumber
    {
        get
        {
            return GlobalRows.Count > 0 ? GlobalRows[^1].Number + 1 : AmkSongList.FirstSongNumber;
        }
    }

    // The number the local table's '+' adds: the one after its last row, or the one after the global table's.
    public int NextLocalNumber
    {
        get
        {
            return LocalRows.Count > 0 ? LocalRows[^1].Number + 1 : NextGlobalNumber;
        }
    }

    // Whether the global table can take its next number, which a song already holding it in the local table forbids.
    public bool CanAddGlobalRow
    {
        get
        {
            if (NextGlobalNumber > AmkSongList.LastSongNumber)
            {
                return false;
            }

            return LocalRows.Count == 0 || LocalRows[0].Number != NextGlobalNumber || LocalRows[0].ListName is null;
        }
    }

    // Whether the local table can take its next number.
    public bool CanAddLocalRow
    {
        get
        {
            return NextLocalNumber <= AmkSongList.LastSongNumber;
        }
    }

    // The rows a list shows: each table a row for every number from its first up to its highest song, the local table starting after the global one's last song.
    public static SongListState FromList(AmkSongList list)
    {
        int firstLocalNumber = list.GlobalSongs.Count > 0 ? list.GlobalSongs.Keys.Max() + 1 : AmkSongList.FirstSongNumber;
        return new SongListState(RowsOf(list.GlobalSongs, AmkSongList.FirstSongNumber), RowsOf(list.LocalSongs, firstLocalNumber));
    }

    // The list the rows hold, the empty rows left out.
    public AmkSongList ToList()
    {
        return new AmkSongList(SongsOf(GlobalRows), SongsOf(LocalRows));
    }

    // Whether the two hold the same rows.
    public bool HoldsSameStateAs(SongListState other)
    {
        return GlobalRows.SequenceEqual(other.GlobalRows) && LocalRows.SequenceEqual(other.LocalRows);
    }

    // The state with a row of either table given a song, or none.
    public SongListState WithSong(int number, string? listName)
    {
        return new SongListState(WithSongIn(GlobalRows, number, listName), WithSongIn(LocalRows, number, listName));
    }

    // The state with a row's song, or its emptiness, moved to another row of its table, the rows between moving one row toward where it came from and
    // every row keeping its number; the same state where the two rows are not in one table.
    public SongListState WithSongMoved(int fromNumber, int toNumber)
    {
        var (rows, fromIndex) = Locate(fromNumber);
        int toIndex = IndexOf(rows, toNumber);
        if (fromIndex < 0 || toIndex < 0)
        {
            return this;
        }

        var listNames = rows.Select(row => row.ListName).ToList();
        string? movedListName = listNames[fromIndex];
        listNames.RemoveAt(fromIndex);
        listNames.Insert(toIndex, movedListName);
        IReadOnlyList<SongListEntry> movedRows = [.. rows.Select((row, index) => row with { ListName = listNames[index] })];
        return ReferenceEquals(rows, GlobalRows) ? new SongListState(movedRows, LocalRows) : new SongListState(GlobalRows, movedRows);
    }

    // Whether a row's clear can do anything: take its song away, or take it away where it is empty and no row after it in its table holds a song.
    public bool CanClear(int number)
    {
        var (rows, index) = Locate(number);
        if (rows[index].ListName is not null)
        {
            return true;
        }

        return !rows.Skip(index + 1).Any(row => row.ListName is not null);
    }

    // The state with a row's song taken away where a row after it in its table holds one, and with the row taken away otherwise - the empty rows after it moving up a number, so it is the table's last
    // number that goes, and a global one going to the start of the local table where that table has rows.
    public SongListState Cleared(int number)
    {
        var (rows, index) = Locate(number);
        if (rows.Skip(index + 1).Any(row => row.ListName is not null))
        {
            return WithSong(number, null);
        }

        var emptied = WithSong(number, null);
        if (ReferenceEquals(rows, LocalRows))
        {
            return new SongListState(GlobalRows, [.. emptied.LocalRows.SkipLast(1)]);
        }

        var lastGlobalRow = emptied.GlobalRows[^1];
        IReadOnlyList<SongListEntry> localRows = LocalRows.Count > 0 ? [lastGlobalRow, .. LocalRows] : LocalRows;
        return new SongListState([.. emptied.GlobalRows.SkipLast(1)], localRows);
    }

    // The state with the global table's next number added as an empty row, the local table's empty row of that number taken away.
    public SongListState WithGlobalRowAdded()
    {
        int number = NextGlobalNumber;
        IReadOnlyList<SongListEntry> localRows = LocalRows.Count > 0 && LocalRows[0].Number == number ? [.. LocalRows.Skip(1)] : LocalRows;
        return new SongListState([.. GlobalRows, new SongListEntry(number, null)], localRows);
    }

    // The state with the local table's next number added as an empty row.
    public SongListState WithLocalRowAdded()
    {
        return new SongListState(GlobalRows, [.. LocalRows, new SongListEntry(NextLocalNumber, null)]);
    }

    // A row for every number from the first up to the highest song, none where there is no song.
    private static List<SongListEntry> RowsOf(IReadOnlyDictionary<int, string> songs, int firstNumber)
    {
        int lastNumber = songs.Count > 0 ? songs.Keys.Max() : firstNumber - 1;
        return [.. Enumerable.Range(firstNumber, lastNumber - firstNumber + 1).Select(number => new SongListEntry(number, songs.GetValueOrDefault(number)))];
    }

    // The songs a table's rows play, by number.
    private static Dictionary<int, string> SongsOf(IReadOnlyList<SongListEntry> rows)
    {
        return rows.Where(row => row.ListName is not null).ToDictionary(row => row.Number, row => row.ListName!);
    }

    // A table's rows with one row given a song, the same rows where the table has no row of that number.
    private static IReadOnlyList<SongListEntry> WithSongIn(IReadOnlyList<SongListEntry> rows, int number, string? listName)
    {
        return [.. rows.Select(row => row.Number == number ? row with { ListName = listName } : row)];
    }

    // The table holding a row of a number, and where the row stands in it, or -1 where neither table has one.
    private (IReadOnlyList<SongListEntry> Rows, int Index) Locate(int number)
    {
        int globalIndex = IndexOf(GlobalRows, number);
        return globalIndex >= 0 ? (GlobalRows, globalIndex) : (LocalRows, IndexOf(LocalRows, number));
    }

    // Where a row of a number stands in a table, or -1 where it has none.
    private static int IndexOf(IReadOnlyList<SongListEntry> rows, int number)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Number == number)
            {
                return i;
            }
        }

        return -1;
    }
}
