using System.Globalization;
using Avalonia.Media;

namespace SpcStudio.Theme;

// The one font family the app draws with, the typeface the custom-drawn controls draw their labels in, and the tabular figures a counting readout asks for.
public static class AppFonts
{
    private const string UiFamilies = "avares://SpcStudio/Assets/Fonts#Inter, Inter, Segoe UI, Ubuntu, Helvetica Neue";

    // Everything the app draws: labels, buttons, menus and the fields alike.
    public static readonly FontFamily Ui = FontFamily.Parse(UiFamilies);

    // What the custom-drawn controls draw their labels with.
    public static readonly Typeface UiTypeface = new(Ui, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);

    // Tabular figures: one advance per digit, for a readout that counts.
    public static readonly FontFeatureCollection Tabular = new() { FontFeature.Parse("+tnum") };

    // A line of text in the app's own typeface at a size and in a brush, to draw or to measure.
    public static FormattedText UiText(string text, double fontSize, IBrush? brush)
    {
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiTypeface, fontSize, brush);
    }

    // How wide the widest of some lines of text comes out in the app's own typeface at a size, rounded up to a whole pixel.
    public static double WidestUiTextWidth(IEnumerable<string> texts, double fontSize)
    {
        return WidestWidth(texts, text => UiText(text, fontSize, null));
    }

    // How wide the widest of some lines of text comes out in the app's own typeface at a size with tabular figures, rounded up to a whole pixel.
    public static double WidestTabularTextWidth(IEnumerable<string> texts, double fontSize)
    {
        return WidestWidth(texts, text => TabularText(text, fontSize));
    }

    // How wide the widest of some lines of text comes out laid out one way, rounded up to a whole pixel.
    private static double WidestWidth(IEnumerable<string> texts, Func<string, FormattedText> layOut)
    {
        return Math.Ceiling(texts.Max(text => layOut(text).Width));
    }

    // A line of text in the app's own typeface at a size with tabular figures, to measure.
    private static FormattedText TabularText(string text, double fontSize)
    {
        var formattedText = UiText(text, fontSize, null);
        formattedText.SetFontFeatures(Tabular);
        return formattedText;
    }
}
