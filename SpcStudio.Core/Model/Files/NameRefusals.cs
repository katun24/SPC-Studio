namespace SpcStudio.Model;

// The messages a name is refused with: left empty, ending in the dot Windows drops, or held by another sample, instrument or folder already.
public static class NameRefusals
{
    public const string SampleNoun = "sample";
    public const string InstrumentNoun = "instrument";
    public const string FolderNoun = "folder";

    // The refusal of a name left empty.
    public static string Empty(NamedThing thing)
    {
        return $"{Possessive(thing)} name cannot be empty.";
    }

    // The refusal of a name ending in a dot.
    public static string EndsInDot(NamedThing thing)
    {
        return $"{Possessive(thing)} name cannot end in a dot.";
    }

    // The refusal of a name that is kept for something else.
    public static string Reserved(NamedThing thing, string name)
    {
        return $"{WithArticle(thing)} cannot be called '{name}'.";
    }

    // The refusal of a name another sample or instrument in the song holds.
    public static string TakenInSong(NamedThing thing, string name)
    {
        return $"{Called(thing, name)} is already in this song.";
    }

    // The refusal of a name another instrument file or folder in the same folder holds.
    public static string TakenInFolder(NamedThing thing, string name)
    {
        return $"{Called(thing, name)} is already in that folder.";
    }

    // The refusal of a copy's name another instrument file or folder in the same folder holds.
    public static string FolderAlreadyContains(NamedThing thing, string name)
    {
        return $"Folder already contains {WithLowerArticle(thing)} called '{name}'.";
    }

    // The refusal a file or folder name gets before anything else is asked of it - left empty or ending in a dot - or null where it gets none.
    public static string? OfDiskName(NamedThing thing, string name)
    {
        if (name.Length == 0)
        {
            return Empty(thing);
        }

        if (DiskNameRules.EndsInDroppedDot(name))
        {
            return EndsInDot(thing);
        }

        return null;
    }

    // What a thing is called in front of its own name: a sample called, an instrument called.
    private static string Called(NamedThing thing, string name)
    {
        return $"{WithArticle(thing)} called {name}";
    }

    // What a thing's name is said as belonging to: a sample's, an instrument's.
    private static string Possessive(NamedThing thing)
    {
        return $"{WithArticle(thing)}'s";
    }

    // What a thing is called in the middle of a sentence: sample, instrument, folder.
    public static string NounOf(NamedThing thing)
    {
        return thing switch
        {
            NamedThing.Sample => SampleNoun,
            NamedThing.Instrument => InstrumentNoun,
            _ => FolderNoun,
        };
    }

    // The thing with its article in front, opening a sentence.
    private static string WithArticle(NamedThing thing)
    {
        string withLowerArticle = WithLowerArticle(thing);
        return char.ToUpperInvariant(withLowerArticle[0]) + withLowerArticle[1..];
    }

    // The thing with its article in front, in the middle of a sentence.
    private static string WithLowerArticle(NamedThing thing)
    {
        return IndefiniteArticle.Before(NounOf(thing));
    }
}
