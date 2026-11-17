namespace SpcStudio.Controls;

// Groups of a list's items laid out one after another with a divider between them, as a menu's and a drop-down's are.
public static class DividedGroups
{
    // The groups' items one after another, a divider between every two groups that hold any.
    public static List<TItem> Join<TItem>(IEnumerable<IEnumerable<TItem>> groups, Func<TItem> newDivider)
    {
        var items = new List<TItem>();
        foreach (var group in groups)
        {
            var groupItems = group.ToList();
            if (groupItems.Count == 0)
            {
                continue;
            }

            if (items.Count > 0)
            {
                items.Add(newDivider());
            }

            items.AddRange(groupItems);
        }

        return items;
    }
}
