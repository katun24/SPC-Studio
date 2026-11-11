using Avalonia;
using Avalonia.Media.TextFormatting;
using SpcStudio.Controls;

namespace SpcStudio.Theme;

// The margins of a read-only panel row: the one above and below its label and value, which makes one line of text stand as tall as a field, and the one
// under a row that stands as far from the next as a list's rows do.
public static class ReadoutMargin
{
    private const string ReadoutMarginKey = "ReadoutMargin";
    private const string ListPitchReadoutRowMarginKey = "ListPitchReadoutRowMargin";
    private const string MeasuredText = "X";

    // Measures one line of the app's text and puts the margin that centers it in a field's height, and the margin that steps rows a list row apart, into the theme.
    public static void AddToTheme(Application app)
    {
        double lineHeight = new TextLayout(MeasuredText, AppFonts.UiTypeface, ThemeResource.UiFontSize, null).Height;
        double marginHeight = (ThemeResource.FieldHeight - lineHeight) / 2;
        app.Resources[ReadoutMarginKey] = new Thickness(0, marginHeight, 0, marginHeight);
        app.Resources[ListPitchReadoutRowMarginKey] = new Thickness(0, 0, 0, ListRows.RowHeight - ThemeResource.FieldHeight);
    }
}
