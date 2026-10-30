using Avalonia.Controls;
using Avalonia.Media;

namespace SpcStudio.Controls;

// The second type of tooltip: a value kept on one line and cut short with an ellipsis shows its whole text in a tooltip while it is cut short, and
// none while it fits.
public static class CutOffTextTips
{
    // Keeps a text on one line, cut short with an ellipsis where it does not fit, showing its whole text in a tooltip whenever it is cut.
    public static void CutOffWithTip(TextBlock text)
    {
        text.TextWrapping = TextWrapping.NoWrap;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.LayoutUpdated += (_, _) => ShowTipWhileCutOff(text);
    }

    // Gives the text its whole self as a tooltip where its last layout cut it short, and takes the tooltip off where it did not.
    private static void ShowTipWhileCutOff(TextBlock text)
    {
        object? tip = text.TextLayout.TextLines.Any(line => line.HasCollapsed) ? text.Text : null;
        if (!Equals(ToolTip.GetTip(text), tip))
        {
            ToolTip.SetTip(text, tip);
        }
    }
}
