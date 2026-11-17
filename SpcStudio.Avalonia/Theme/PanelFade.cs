using System;
using System.Linq;
using Avalonia.Data.Converters;

namespace SpcStudio.Theme;

// The fades where a panel's content moves out of sight, at the top under its header and at the bottom, each clear while there is nothing past it.
public static class PanelFade
{
    private const double FullOpacity = 1;
    private const int FurthestOffsetIndex = 0;
    private const int OffsetIndex = 1;

    // How opaque the top fade is at a scroll offset, growing from clear at the top to full over the fade's own height.
    public static readonly FuncValueConverter<double, double> OpacityAtOffset = new(OpacityAtDistance);

    // How opaque the bottom fade is, from the furthest offset the content scrolls to and the offset it stands at, clear once scrolled to the end.
    public static readonly FuncMultiValueConverter<double, double> OpacityBeforeEnd = new(offsets =>
    {
        double[] furthestAndCurrent = offsets.ToArray();
        return OpacityAtDistance(furthestAndCurrent[FurthestOffsetIndex] - furthestAndCurrent[OffsetIndex]);
    });

    // How opaque a fade is with this much content scrolled past it, full once that reaches the fade's own height.
    private static double OpacityAtDistance(double distance)
    {
        return Math.Min(distance / Palette.PanelFadeHeight, FullOpacity);
    }
}
