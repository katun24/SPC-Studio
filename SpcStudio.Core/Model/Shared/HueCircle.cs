namespace SpcStudio.Model;

// The whole hues a color picked by its hue alone can stand at, in degrees round the color circle.
public static class HueCircle
{
    public const int MaxHue = 359;

    public static readonly ValueRange HueRange = new(0, MaxHue);
}
