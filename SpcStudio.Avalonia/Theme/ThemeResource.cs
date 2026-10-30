using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpcStudio.Theme;

// The one way code reads a value out of the app's theme.
public static class ThemeResource
{
    public const string TextPrimaryBrushKey = "TextPrimaryBrush";
    public const string TextSecondaryBrushKey = "TextSecondaryBrush";
    private const string TextDisabledBrushKey = "TextDisabledBrush";
    private const string BorderBrushKey = "BorderBrush";
    private const string FieldHeightKey = "FieldHeight";
    private const string UiFontSizeKey = "UiFontSize";
    private const string FrameBorderThicknessKey = "FrameBorderThickness";
    private const string SettingsBoxWidthKey = "SettingsBoxWidth";

    // The width of every box as wide as Preferences.
    public static double SettingsBoxWidth
    {
        get
        {
            return Of<double>(SettingsBoxWidthKey);
        }
    }

    // The height everything typed into or pressed is.
    public static double FieldHeight
    {
        get
        {
            return Of<double>(FieldHeightKey);
        }
    }

    // The size the app's text is drawn at.
    public static double UiFontSize
    {
        get
        {
            return Of<double>(UiFontSizeKey);
        }
    }

    // The color text and glyphs are drawn in.
    public static IBrush TextPrimaryBrush
    {
        get
        {
            return Of<IBrush>(TextPrimaryBrushKey);
        }
    }

    // The quieter color labels, readouts and secondary glyphs are drawn in.
    public static IBrush TextSecondaryBrush
    {
        get
        {
            return Of<IBrush>(TextSecondaryBrushKey);
        }
    }

    // The dimmest text color, for text that stands for something out of use.
    public static IBrush TextDisabledBrush
    {
        get
        {
            return Of<IBrush>(TextDisabledBrushKey);
        }
    }

    // The color a control's border and a dividing line are drawn in.
    public static IBrush BorderBrush
    {
        get
        {
            return Of<IBrush>(BorderBrushKey);
        }
    }

    // The stroke a frame is drawn with, the same on every side.
    public static double FrameStroke
    {
        get
        {
            return Of<Thickness>(FrameBorderThicknessKey).Left;
        }
    }

    // One of the theme's values, by its key.
    public static T Of<T>(string key)
    {
        if (Application.Current!.FindResource(key) is T value)
        {
            return value;
        }

        throw new KeyNotFoundException(key);
    }
}
