using Avalonia;

namespace SpcStudio.Theme;

// The one corner rounding every rounded part of the app shares.
public static class Rounding
{
    public const double Radius = 4;

    // All four corners rounded by the shared radius.
    public static readonly CornerRadius AllCorners = new(Radius);

    // The top two corners rounded by the shared radius and the bottom two left square.
    public static readonly CornerRadius TopCorners = new(Radius, Radius, 0, 0);
}
