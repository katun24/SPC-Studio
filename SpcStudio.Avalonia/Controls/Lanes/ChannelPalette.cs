using Avalonia.Media;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// Which color a channel is drawn in, and the color mixing the channel buttons, the roll's instrument colors and the palette are built with.
public static class ChannelPalette
{
    // The color a channel is drawn in, every extra channel sharing one.
    public static Color ColorOfChannel(int channel)
    {
        return Song.IsExtraChannel(channel) ? Palette.SilverGray : Palette.Channels[channel];
    }

    // A color at the given opacity.
    public static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    // An instrument's stored color, packed as red, green and blue bytes in one number, as a color.
    public static Color FromPackedRgb(int packedRgb)
    {
        return Color.FromRgb(PackedRgb.RedOf(packedRgb), PackedRgb.GreenOf(packedRgb), PackedRgb.BlueOf(packedRgb));
    }

    // A color packed as red, green and blue bytes in one number, the way an instrument stores it.
    public static int ToPackedRgb(Color color)
    {
        return PackedRgb.Pack(color.R, color.G, color.B);
    }

    // A color lifted in lightness by the given amount, at the given chroma and hue in degrees, the hue being the instrument color picker's.
    public static Color AtHue(Color color, double lightnessLift, double chroma, double hueDegrees)
    {
        double lightness = Oklch.FromPackedRgb(ToPackedRgb(color)).Lightness;
        return FromOklch(lightness + lightnessLift, chroma, hueDegrees);
    }

    // The color of a lightness, a chroma and a hue in degrees, the hue being the instrument color picker's.
    public static Color FromOklch(double lightness, double chroma, double hueDegrees)
    {
        return FromPackedRgb(Oklch.ToPackedRgb(lightness, chroma, hueDegrees));
    }

    // Mixes a color towards a target by a fraction, nothing of the target at none and all of it at one.
    public static Color Blend(Color color, Color target, double fractionTowardsTarget)
    {
        return Color.FromRgb(
            BlendComponent(color.R, target.R, fractionTowardsTarget),
            BlendComponent(color.G, target.G, fractionTowardsTarget),
            BlendComponent(color.B, target.B, fractionTowardsTarget));
    }

    // Mixes one of a color's three components towards the target's, rounded to the nearest whole value.
    private static byte BlendComponent(byte component, byte targetComponent, double fractionTowardsTarget)
    {
        return (byte)Math.Round(component + (targetComponent - component) * fractionTowardsTarget);
    }
}
