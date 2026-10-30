namespace SpcStudio.Model;

// The Oklab color space in its polar form - lightness, chroma and hue - whose lightness looks the same at every hue, converted to and from packed sRGB.
public static class Oklch
{
    private const double FullTurnDegrees = 360;
    private const double DegreesPerRadian = 180 / Math.PI;
    private const double LinearSegmentEnd = 0.0031308;
    private const double EncodedLinearSegmentEnd = 0.04045;
    private const double LinearSegmentSlope = 12.92;
    private const double CurveOffset = 0.055;
    private const double CurveScale = 1.055;
    private const double CurveExponent = 2.4;

    private static readonly double[,] ConeRootsFromOklab =
    {
        { 1, 0.3963377774, 0.2158037573 },
        { 1, -0.1055613458, -0.0638541728 },
        { 1, -0.0894841775, -1.2914855480 },
    };

    private static readonly double[,] LinearRgbFromCones =
    {
        { 4.0767416621, -3.3077115913, 0.2309699292 },
        { -1.2684380046, 2.6097574011, -0.3413193965 },
        { -0.0041960863, -0.7034186147, 1.7076147010 },
    };

    private static readonly double[,] ConesFromLinearRgb =
    {
        { 0.4122214708, 0.5363325363, 0.0514459929 },
        { 0.2119034982, 0.6806995451, 0.1073969566 },
        { 0.0883024619, 0.2817188376, 0.6299787005 },
    };

    private static readonly double[,] OklabFromConeRoots =
    {
        { 0.2104542553, 0.7936177850, -0.0040720468 },
        { 1.9779984951, -2.4285922050, 0.4505937099 },
        { 0.0259040371, 0.7827717662, -0.8086757660 },
    };

    // A color of a lightness, a chroma and a hue in degrees as packed sRGB, each channel held to its range.
    public static int ToPackedRgb(double lightness, double chroma, double hueDegrees)
    {
        double hueRadians = hueDegrees / DegreesPerRadian;
        var coneRoots = Transform(ConeRootsFromOklab, (lightness, chroma * Math.Cos(hueRadians), chroma * Math.Sin(hueRadians)));
        var cones = (Math.Pow(coneRoots.X, 3), Math.Pow(coneRoots.Y, 3), Math.Pow(coneRoots.Z, 3));
        var linearRgb = Transform(LinearRgbFromCones, cones);
        return PackedRgb.Pack(EncodeChannel(linearRgb.X), EncodeChannel(linearRgb.Y), EncodeChannel(linearRgb.Z));
    }

    // A packed sRGB color's lightness, its chroma, and its hue in degrees from zero up to a full turn.
    public static (double Lightness, double Chroma, double HueDegrees) FromPackedRgb(int packedRgb)
    {
        var linearRgb = (DecodeChannel(PackedRgb.RedOf(packedRgb)), DecodeChannel(PackedRgb.GreenOf(packedRgb)), DecodeChannel(PackedRgb.BlueOf(packedRgb)));
        var cones = Transform(ConesFromLinearRgb, linearRgb);
        var oklab = Transform(OklabFromConeRoots, (Math.Cbrt(cones.X), Math.Cbrt(cones.Y), Math.Cbrt(cones.Z)));
        double hueDegrees = Math.Atan2(oklab.Z, oklab.Y) * DegreesPerRadian;
        return (oklab.X, Math.Sqrt(oklab.Y * oklab.Y + oklab.Z * oklab.Z), hueDegrees < 0 ? hueDegrees + FullTurnDegrees : hueDegrees);
    }

    // A three-by-three matrix applied to a column of three values.
    private static (double X, double Y, double Z) Transform(double[,] matrix, (double X, double Y, double Z) column)
    {
        return (
            matrix[0, 0] * column.X + matrix[0, 1] * column.Y + matrix[0, 2] * column.Z,
            matrix[1, 0] * column.X + matrix[1, 1] * column.Y + matrix[1, 2] * column.Z,
            matrix[2, 0] * column.X + matrix[2, 1] * column.Y + matrix[2, 2] * column.Z);
    }

    // A linear light level as an sRGB byte, rounded and held to the byte's range.
    private static byte EncodeChannel(double linearLevel)
    {
        double level = Math.Clamp(linearLevel, 0, 1);
        double encodedLevel = level <= LinearSegmentEnd ? level * LinearSegmentSlope : CurveScale * Math.Pow(level, 1 / CurveExponent) - CurveOffset;
        return (byte)Math.Round(Math.Clamp(encodedLevel, 0, 1) * byte.MaxValue);
    }

    // An sRGB byte as a linear light level.
    private static double DecodeChannel(byte channel)
    {
        double encodedLevel = channel / (double)byte.MaxValue;
        return encodedLevel <= EncodedLinearSegmentEnd ? encodedLevel / LinearSegmentSlope : Math.Pow((encodedLevel + CurveOffset) / CurveScale, CurveExponent);
    }
}
