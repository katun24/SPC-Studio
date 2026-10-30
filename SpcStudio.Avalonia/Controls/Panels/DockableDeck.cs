using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// Panels under one strip of tabs, with a button at the strip's right end that takes them all out into a window of their own or puts them back.
public abstract class DockableDeck<TTab> : TabDeck<TTab> where TTab : struct, Enum
{
    private const double PanelBorderThickness = 1;

    private readonly Button _cornerButton;
    private readonly IconShape _cornerIcon;

    // Builds the tabs and the corner button, standing as far in from the panels' right edge as a panel's own corner button.
    protected DockableDeck(IEnumerable<(TTab Tab, string Label)> tabs) : base(tabs)
    {
        _cornerIcon = new IconShape();
        _cornerIcon.Bind(Shape.FillProperty, _cornerIcon.GetResourceObservable(ThemeResource.TextSecondaryBrushKey));

        _cornerButton = new Button { Content = _cornerIcon, Margin = new Thickness(0, Space.Px2, 0, 0) };
        _cornerButton.Classes.Add(ThemeClasses.PanelCorner);
        _cornerButton.Click += (_, _) => CornerPressed?.Invoke();
        DoesCornerDock = false;

        CornerSlot.Child = _cornerButton;
        CornerSlot.Padding = new Thickness(0, 0, Space.Px16 + PanelBorderThickness, 0);
    }

    // Raised by the button at the right end of the strip.
    public event Action? CornerPressed;

    // Whether the corner button puts the deck back where it was taken from rather than taking it out, which its glyph says.
    public bool DoesCornerDock
    {
        set
        {
            PanelCornerButton.ShowState(_cornerButton, _cornerIcon, value, Tooltips.PopOutSidePanel, Tooltips.DockIntoSidePanel);
        }
    }
}
