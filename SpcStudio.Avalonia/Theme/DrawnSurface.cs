using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace SpcStudio.Theme;

// The fill every custom-drawn surface stands on - the roll, its keys, the ruler, the lanes, the envelope graph, the waveform and the SPC Player's canvas -
// the two lines the roll's grid and every ruler's ticks are drawn in, and where a one-pixel line is put to be drawn sharp.
public static class DrawnSurface
{
    // How far into a device pixel a one-pixel line is put, so it covers that pixel alone.
    public const double HalfPixel = 0.5;

    // The fill under every custom-drawn surface.
    public static readonly IBrush Background = new ImmutableSolidColorBrush(Palette.DrawnSurfaceBackground);

    // The roll's plain grid line and a ruler's short tick.
    public static readonly IPen GridLinePen = new ImmutablePen(new ImmutableSolidColorBrush(Palette.GridLine), 1);

    // The roll's bar line and a ruler's long tick.
    public static readonly IPen BarLinePen = new ImmutablePen(new ImmutableSolidColorBrush(Palette.BarLine), 1);

    // A position moved to the middle of the device pixel it falls in, so a one-pixel line drawn there is sharp.
    public static double PixelCenter(double position)
    {
        return Math.Round(position) + HalfPixel;
    }
}
