using System.Text.Json;

namespace SpcStudio.Web;

// One file on SMWCentral as its API writes it, with the fields of its own its author filled in.
public sealed record SmwcFile(int Id, string Section, string Name, long Time, List<SmwcAuthor> Authors, string DownloadUrl, JsonElement RawFields)
{
    public const string MusicSection = "smwmusic";
    private const string TypeField = "type";
    private const string SongType = "song";

    // Whether the file is a song rather than a soundtrack, a sound effect or anything outside the music section.
    public bool IsSong
    {
        get
        {
            return Section == MusicSection && RawField(TypeField) == SongType;
        }
    }

    // One of the fields the author filled in, or null where the file has no such field.
    public string? RawField(string name)
    {
        if (RawFields.ValueKind != JsonValueKind.Object || !RawFields.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return field.GetString();
    }
}

// One author of a file.
public sealed record SmwcAuthor(string? Name);

// One page of SMWCentral's listing of the music section: the files on it and how many pages there are.
public sealed record SmwcListingPage(int LastPage, List<SmwcFile> Data);
