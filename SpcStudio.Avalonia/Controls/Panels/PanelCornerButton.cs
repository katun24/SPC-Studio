using Avalonia.Controls;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// What a panel's corner button shows: popping the panel out where it stands in a deck, docking it back where it stands in a window of its own.
public static class PanelCornerButton
{
    // Draws the button's glyph and gives it the tooltip for whether the panel stands in a window of its own.
    public static void ShowState(Button button, IconShape icon, bool isPoppedOut, string popOutTooltip, string dockTooltip)
    {
        icon.Data = isPoppedOut ? AppIcons.ArrowCollapseRight : AppIcons.OpenInNew;
        ToolTip.SetTip(button, isPoppedOut ? dockTooltip : popOutTooltip);
    }
}
