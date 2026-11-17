using Avalonia.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// One of the main window's four right-hand panels and where it stands: in the column's deck, in the deck of the column's own window, or in a window of its own.
public sealed class PanelSlot(Control panel, PanelTab tab, string title, Func<PanelLayout, double> savedWindowWidthIn)
{
    public Control Panel { get; } = panel;
    public PanelTab Tab { get; } = tab;
    public string Title { get; } = title;

    // The panel's own window, where it stands in one.
    public PanelWindow? Window { get; set; }

    // Where the settings said the panel stood when they were read, put back once the main window has been shown.
    public PanelPlacement SavedPlacement { get; set; } = new();

    // Whether the panel stands in a window of its own.
    public bool IsPoppedOut
    {
        get
        {
            return Window is not null;
        }
    }

    // How wide a song's layout left the panel's own window, 0 where it says nothing.
    public double SavedWindowWidthIn(PanelLayout layout)
    {
        return savedWindowWidthIn(layout);
    }
}
