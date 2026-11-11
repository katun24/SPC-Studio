namespace SpcStudio;

// A count written out with its noun, the noun in the plural wherever the count is not one.
public static class CountedNoun
{
    public const string NoteNoun = "note";
    private const string PluralEnding = "s";

    // The count and its noun, such as "1 note" or "3 notes".
    public static string Of(int count, string noun)
    {
        return $"{count} {NounFor(count, noun)}";
    }

    // The noun alone, in the plural wherever the count is not one.
    public static string NounFor(int count, string noun)
    {
        return WordFor(count, noun, noun + PluralEnding);
    }

    // The word that agrees with a count: the singular one for one and the plural one for any other count.
    public static string WordFor(int count, string singularWord, string pluralWord)
    {
        if (count == 1)
        {
            return singularWord;
        }

        return pluralWord;
    }
}
