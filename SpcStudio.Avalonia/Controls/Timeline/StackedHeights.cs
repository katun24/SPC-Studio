namespace SpcStudio.Controls;

// The heights of rows stacked one over another, where one row is zoomed and the rows beside it give way.
public static class StackedHeights
{
    // Zooms one row's height by a factor, held to a row's range and to what the other rows and a further room can give, the others giving way in
    // proportion to their room, and answers whether anything moved.
    public static bool TryZoomOne(double[] heights, int zoomedIndex, double zoomFactor, double minHeight, double maxHeight, double roomBeyond)
    {
        double heightBefore = heights[zoomedIndex];
        double heightOthersCanGive = IndexesBesides(heights, zoomedIndex).Sum(index => heights[index] - minHeight);
        double ceiling = Math.Min(maxHeight, heightBefore + heightOthersCanGive + roomBeyond);
        double heightAfter = Math.Clamp(heightBefore * zoomFactor, minHeight, Math.Max(heightBefore, ceiling));
        if (heightAfter == heightBefore)
        {
            return false;
        }

        heights[zoomedIndex] = heightAfter;
        SpreadOverOthers(heights, zoomedIndex, heightBefore - heightAfter, minHeight, maxHeight);
        return true;
    }

    // Adds a height to every row but one, or takes it away where it is negative, each row's share in proportion to the room it has in that direction.
    private static void SpreadOverOthers(double[] heights, int skippedIndex, double height, double minHeight, double maxHeight)
    {
        Func<double, double> roomOf = height > 0 ? rowHeight => maxHeight - rowHeight : rowHeight => rowHeight - minHeight;
        var others = IndexesBesides(heights, skippedIndex).ToList();
        double room = others.Sum(index => roomOf(heights[index]));
        if (room <= 0)
        {
            return;
        }

        double heightMoved = Math.Min(Math.Abs(height), room) * Math.Sign(height);
        foreach (int index in others)
        {
            heights[index] = Math.Clamp(heights[index] + heightMoved * roomOf(heights[index]) / room, minHeight, maxHeight);
        }
    }

    // Every row's index but one.
    private static IEnumerable<int> IndexesBesides(double[] heights, int skippedIndex)
    {
        return Enumerable.Range(0, heights.Length).Where(index => index != skippedIndex);
    }
}
