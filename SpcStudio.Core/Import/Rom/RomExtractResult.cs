namespace SpcStudio.Import;

// What became of one song slot in an extraction: the song file written and the warnings its import gave, or why the song was skipped.
public sealed record RomExtractResult(int Slot, string? Path, string? Reason, IReadOnlyList<string> Warnings)
{
    // Whether the song was written rather than skipped.
    public bool IsWritten
    {
        get
        {
            return Path is not null;
        }
    }
}
