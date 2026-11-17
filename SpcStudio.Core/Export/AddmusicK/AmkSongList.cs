using System.Globalization;
using System.IO;
using System.Text;
using SpcStudio.Import;

namespace SpcStudio.Export;

// AddmusicK's song list: the file each song number plays, the global songs apart from the local ones, read the way AddmusicK reads it and written back in its own layout.
public sealed class AmkSongList
{
    public const string FileName = "Addmusic_list.txt";
    public const int FirstSongNumber = 0x01;
    public const int LastSongNumber = 0xFE;
    private const string GlobalsLabel = "Globals:";
    private const string LocalsLabel = "Locals:";
    private const string NumberFormat = "X2";
    private const string NumberToNameGap = "  ";
    private const int MaxNumberDigits = 2;

    // A list holding these songs, each by its number.
    public AmkSongList(IReadOnlyDictionary<int, string> globalSongs, IReadOnlyDictionary<int, string> localSongs)
    {
        GlobalSongs = new SortedDictionary<int, string>(globalSongs.ToDictionary());
        LocalSongs = new SortedDictionary<int, string>(localSongs.ToDictionary());
    }

    // The global songs' files by number, named relative to the install's music folder.
    public IReadOnlyDictionary<int, string> GlobalSongs { get; }

    // The local songs' files by number, named relative to the install's music folder.
    public IReadOnlyDictionary<int, string> LocalSongs { get; }

    // The list file inside an install.
    public static string PathIn(string amkRoot)
    {
        return Path.Combine(amkRoot, FileName);
    }

    // The full path of a song the list names.
    public static string SongPath(string amkRoot, string listName)
    {
        return Path.Combine(amkRoot, AmkInstallFolders.Music, listName);
    }

    // Reads an install's list, refusing a list AddmusicK would refuse and a number no song can have.
    public static AmkSongList Read(string amkRoot)
    {
        string text;
        try
        {
            text = File.ReadAllText(PathIn(amkRoot));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw AmkSongListException.NotRead(PathIn(amkRoot), exception.Message);
        }

        return Parse(text);
    }

    // Writes the list into an install, over the list already there.
    public void Write(string amkRoot)
    {
        var text = new StringBuilder();
        AppendSection(text, GlobalsLabel, GlobalSongs);
        AppendSection(text, LocalsLabel, LocalSongs);
        try
        {
            File.WriteAllText(PathIn(amkRoot), text.ToString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw AmkSongListException.NotWritten(PathIn(amkRoot), exception.Message);
        }
    }

    // Appends a section's label and then one line per song.
    private static void AppendSection(StringBuilder text, string label, IReadOnlyDictionary<int, string> songs)
    {
        text.Append(label).Append(Environment.NewLine);
        foreach (var (number, name) in songs)
        {
            text.Append(number.ToString(NumberFormat)).Append(NumberToNameGap).Append(name).Append(Environment.NewLine);
        }
    }

    // The list a text holds, read a character at a time as AddmusicK reads it.
    private static AmkSongList Parse(string text)
    {
        var globalSongs = new Dictionary<int, string>();
        var localSongs = new Dictionary<int, string>();
        Dictionary<int, string>? section = null;
        int position = 0;
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
                continue;
            }

            if (AmkTextReader.IsSpelledAt(text, position, GlobalsLabel))
            {
                section = globalSongs;
                position += GlobalsLabel.Length;
                continue;
            }

            if (AmkTextReader.IsSpelledAt(text, position, LocalsLabel))
            {
                section = localSongs;
                position += LocalsLabel.Length;
                continue;
            }

            if (section is null)
            {
                throw AmkSongListException.NoGlobalsLabel();
            }

            int number = ReadNumber(text, ref position);
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }

            int nameStart = position;
            position = AmkWhitespace.LineEndFrom(text, position);
            if (position == text.Length)
            {
                break;
            }

            section[number] = text[nameStart..position];
        }

        RefuseLocalNotAboveGlobals(globalSongs, localSongs);
        return new AmkSongList(globalSongs, localSongs);
    }

    // The song number at a position, in hex followed by white space, refusing anything else and a number no song can have.
    private static int ReadNumber(string text, ref int position)
    {
        int start = position;
        while (position < text.Length && position - start < MaxNumberDigits && Uri.IsHexDigit(text[position]))
        {
            position++;
        }

        if (position == start || position == text.Length || !char.IsWhiteSpace(text[position]))
        {
            throw AmkSongListException.InvalidNumber(text[start..AmkWhitespace.LineEndFrom(text, start)]);
        }

        int number = int.Parse(text[start..position], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (number < FirstSongNumber || number > LastSongNumber)
        {
            throw AmkSongListException.NumberOutOfRange(number);
        }

        return number;
    }

    // Refuses a list with a local song numbered at or below a global one.
    private static void RefuseLocalNotAboveGlobals(Dictionary<int, string> globalSongs, Dictionary<int, string> localSongs)
    {
        if (globalSongs.Count == 0 || localSongs.Count == 0)
        {
            return;
        }

        int lowestLocal = localSongs.Keys.Min();
        if (lowestLocal <= globalSongs.Keys.Max())
        {
            throw AmkSongListException.LocalNotAboveGlobals(lowestLocal);
        }
    }
}
