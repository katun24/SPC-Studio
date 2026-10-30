namespace SpcStudio.Model;

// The colors an instrument is given: one for each whole hue, all at one lightness and one chroma, as 0xRRGGBB - and which hue a new instrument takes.
public static class InstrumentPalette
{
    public const int MaxHue = 359;
    private const int HueCount = MaxHue + 1;
    private const double Lightness = 0.74;
    private const double Chroma = 0.12;
    private const double MaxGrayChroma = 0.03;

    private static readonly int[] ColorsOfHues = [.. Enumerable.Range(0, HueCount).Select(hue => Oklch.ToPackedRgb(Lightness, Chroma, hue))];
    private static readonly Dictionary<int, int> LowestHueOfEachColor = Enumerable.Range(0, HueCount)
        .GroupBy(hue => ColorsOfHues[hue])
        .ToDictionary(hues => hues.Key, hues => hues.Min());

    // The color of a whole hue.
    public static int ColorOfHue(int hue)
    {
        return ColorsOfHues[hue];
    }

    // The whole hue a color stands at, or null for a gray, which has none.
    public static int? HueOf(int colorRgb)
    {
        if (LowestHueOfEachColor.TryGetValue(colorRgb, out int hueOnScale))
        {
            return hueOnScale;
        }

        var (_, chroma, hueDegrees) = Oklch.FromPackedRgb(colorRgb);
        if (chroma <= MaxGrayChroma)
        {
            return null;
        }

        return (int)Math.Round(hueDegrees) % HueCount;
    }

    // The color a new instrument takes beside instruments of the given colors: the hue furthest from every hue they stand at.
    public static int ColorFurthestFrom(IEnumerable<int> colorsRgb)
    {
        return ColorOfHue(HueFurthestFrom([.. colorsRgb.Select(HueOf).OfType<int>()]));
    }

    // The color of the instrument created at that index into a song whose instruments all kept the color they were given.
    public static int ColorForInstrumentIndex(int instrumentIndex)
    {
        var hues = new List<int>();
        for (int index = 0; index <= instrumentIndex; index++)
        {
            hues.Add(HueFurthestFrom(hues));
        }

        return ColorOfHue(hues[^1]);
    }

    // The whole hue furthest round the circle from every hue given, the lowest where several are equally far.
    private static int HueFurthestFrom(IReadOnlyCollection<int> usedHues)
    {
        int furthestHue = 0;
        int furthestDistance = -1;
        for (int hue = 0; hue <= MaxHue; hue++)
        {
            int distanceToNearestUsedHue = usedHues.Count == 0 ? HueCount : usedHues.Min(usedHue => DistanceRoundCircle(hue, usedHue));
            if (distanceToNearestUsedHue > furthestDistance)
            {
                furthestHue = hue;
                furthestDistance = distanceToNearestUsedHue;
            }
        }

        return furthestHue;
    }

    // How far apart two hues are, going round the circle whichever way is shorter.
    private static int DistanceRoundCircle(int firstHue, int secondHue)
    {
        int distance = Math.Abs(firstHue - secondHue);
        return Math.Min(distance, HueCount - distance);
    }
}
