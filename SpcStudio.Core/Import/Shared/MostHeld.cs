namespace SpcStudio.Import;

// The distinct values among a set of items, each with how many items hold it, the value most held first.
public static class MostHeld
{
    // One item for each distinct value, as the first item holding it, with how many hold it, the most held first and equal counts in the order first held.
    public static List<(T Item, int Count)> DistinctByCount<T>(IEnumerable<T> items, Func<T, T, bool> isSameValue)
    {
        var distinctItems = new List<(T Item, int Count)>();
        foreach (var item in items)
        {
            int distinctIndex = distinctItems.FindIndex(distinctItem => isSameValue(distinctItem.Item, item));
            if (distinctIndex < 0)
            {
                distinctItems.Add((item, 1));
                continue;
            }

            distinctItems[distinctIndex] = (distinctItems[distinctIndex].Item, distinctItems[distinctIndex].Count + 1);
        }

        return [.. distinctItems.OrderByDescending(distinctItem => distinctItem.Count)];
    }
}
