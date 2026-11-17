using System.Globalization;

namespace SpcStudio;

// Percentages: how many make a whole, and how a readout shows one.
public static class Percent
{
    public const double PerWhole = 100;
    private const string Sign = "%";

    // A percentage as a readout shows it, rounded to a whole number.
    public static string Text(double percent)
    {
        return Math.Round(percent).ToString(CultureInfo.InvariantCulture) + Sign;
    }
}
