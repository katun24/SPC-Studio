using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SpcStudio.Controls;

// The second type of tooltip: a value kept on one line and cut short with an ellipsis shows its whole text in a tooltip while it is cut short, and
// none while it fits.
public static class CutOffTextTips
{
    // Cuts off this way the text of every drop-down's and every menu's item, present and future.
    public static void CutOffEveryListItem()
    {
        Control.LoadedEvent.AddClassHandler<TextBlock>((text, _) => CutOffWhereListItem(text));
    }

    // Keeps a text on one line, cut short with an ellipsis where it does not fit, showing its whole text in a tooltip whenever it is cut.
    public static void CutOffWithTip(TextBlock text)
    {
        text.TextWrapping = TextWrapping.NoWrap;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.LayoutUpdated += (_, _) => ShowTipWhileCutOff(text);
    }

    // Cuts off a text standing in a drop-down's or a menu's item, unless it is cut off already.
    private static void CutOffWhereListItem(TextBlock text)
    {
        if (text.TextTrimming == TextTrimming.CharacterEllipsis || text.FindAncestorOfType<ComboBoxItem>() is null && text.FindAncestorOfType<MenuItem>() is null)
        {
            return;
        }

        CutOffWithTip(text);
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
