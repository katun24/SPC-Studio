namespace SpcStudio.Model;

// The one search in halves every ordered list is read through.
public static class SortedSearch
{
    // How many items at the front of a list a test holds for, the list being in an order the test holds for up to some point and for none after it.
    public static int CountLeading<T>(IReadOnlyList<T> items, Func<T, bool> isLeading)
    {
        int lowIndex = 0;
        int highIndex = items.Count;
        while (lowIndex < highIndex)
        {
            int middleIndex = lowIndex + (highIndex - lowIndex) / 2;
            if (isLeading(items[middleIndex]))
            {
                lowIndex = middleIndex + 1;
            }
            else
            {
                highIndex = middleIndex;
            }
        }

        return lowIndex;
    }
}
