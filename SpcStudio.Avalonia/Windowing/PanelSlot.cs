using Avalonia.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// One of the main window's three right-hand panels and where it stands: in the column's deck, in the deck of the column's own window, or in a window of its own.
public sealed class PanelSlot(Control panel, PanelTab tab, string title, Action<bool> showCornerButton)
{
    public Control Panel { get; } = panel;
    public PanelTab Tab { get; } = tab;
    public string Title { get; } = title;

    // Draws the panel's corner button for whether the panel stands in a window of its own.
    public Action<bool> ShowCornerButton { get; } = showCornerButton;

    // The panel's own window, where it stands in one.
    public PanelWindow? Window { get; set; }

    // Whether the panel stands in a window of its own.
    public bool IsPoppedOut
    {
        get
        {
            return Window is not null;
        }
    }
}
