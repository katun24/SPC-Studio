using SpcStudio.Model;

namespace SpcStudio.Controls;

// How a hue strip's whole hues map to the colors they are drawn in, packed as red, green and blue bytes in one number, and those colors back to their hues.
public sealed record HueScale(Func<int, int> ColorOfHue, Func<int, int?> HueOf)
{
    public static readonly HueScale InstrumentHues = new(InstrumentPalette.ColorOfHue, InstrumentPalette.HueOf);
}
