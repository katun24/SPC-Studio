namespace SpcStudio.Model;

// How a sentence lists several items.
public static class SentenceList
{
    private const string ItemSeparator = ", ";

    // The items joined into one list, in the order given.
    public static string Of(IEnumerable<string> items)
    {
        return string.Join(ItemSeparator, items);
    }
}
