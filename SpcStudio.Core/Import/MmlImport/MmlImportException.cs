namespace SpcStudio.Import;

// Why a song was refused, and the line it was refused at - zero where the reason belongs to the song as a whole. Every reason is made by one of this
// type's factories, which stand in one file per area of the import.
public sealed partial class MmlImportException : Exception
{
    // A refusal for a reason, with the line it was refused at in front of it where there is one.
    private MmlImportException(string reason, int line) : base(line > 0 ? $"Line {line}: {reason}" : reason)
    {
        Line = line;
        Reason = reason;
    }

    public int Line { get; }
    public string Reason { get; }

    // A phrase that starts a reason, its first character capitalized and nothing else touched.
    private static string AsSentenceStart(string phrase)
    {
        if (phrase.Length == 0)
        {
            return phrase;
        }

        return char.ToUpperInvariant(phrase[0]) + phrase[1..];
    }
}
