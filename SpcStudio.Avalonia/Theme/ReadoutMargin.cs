using Avalonia;
using Avalonia.Media.TextFormatting;

namespace SpcStudio.Theme;

// The margin above and below a read-only panel row's label and value, which makes one line of text stand as tall as a field.
public static class ReadoutMargin
{
    private const string ReadoutMarginKey = "ReadoutMargin";
    private const string MeasuredText = "X";

    // Measures one line of the app's text and puts the margin that centers it in a field's height into the theme.
    public static void AddToTheme(Application app)
    {
        double lineHeight = new TextLayout(MeasuredText, AppFonts.UiTypeface, ThemeResource.UiFontSize, null).Height;
        double marginHeight = (ThemeResource.FieldHeight - lineHeight) / 2;
        app.Resources[ReadoutMarginKey] = new Thickness(0, marginHeight, 0, marginHeight);
    }
}
