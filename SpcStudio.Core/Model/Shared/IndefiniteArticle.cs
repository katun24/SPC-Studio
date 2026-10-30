namespace SpcStudio.Model;

// The article a sentence puts in front of a noun it names one of.
public static class IndefiniteArticle
{
    private const string VowelsTakingAn = "aeiou";

    // The noun with "a" or "an" in front, as its first letter asks.
    public static string Before(string noun)
    {
        string article = VowelsTakingAn.Contains(char.ToLowerInvariant(noun[0])) ? "an" : "a";
        return $"{article} {noun}";
    }
}
