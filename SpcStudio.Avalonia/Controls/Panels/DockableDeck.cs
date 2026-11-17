using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// Panels under one strip of tabs, with a button at the strip's right end that takes them all out into a window of their own, and room left of it for a
// deck's own buttons of the same look.
public abstract class DockableDeck<TTab> : TabDeck<TTab> where TTab : struct, Enum
{
    private const double PanelBorderThickness = 1;

    private readonly StackPanel _cornerButtons = new() { Orientation = Orientation.Horizontal, Spacing = Space.Px8, Margin = new Thickness(0, Space.Px2, 0, 0) };
    private readonly Button _cornerButton;

    // Builds the tabs and the corner button, standing above the right edge of the fields in the panels under it.
    protected DockableDeck(IEnumerable<(TTab Tab, string Label)> tabs) : base(tabs)
    {
        _cornerButton = AddCornerButton(AppIcons.OpenInNew, () => CornerPressed?.Invoke());
        ToolTip.SetTip(_cornerButton, Tooltips.PopOutSidePanel);

        CornerSlot.Child = _cornerButtons;
        CornerSlot.Padding = new Thickness(0, 0, Space.Px16 + PanelBorderThickness, 0);
    }

    // Raised by the button at the right end of the strip.
    public event Action? CornerPressed;

    // Whether the corner button stands at the right end of the strip at all.
    public bool IsCornerButtonShown
    {
        set
        {
            _cornerButton.IsVisible = value;
        }
    }

    // Puts a button of the corner button's look left of the buttons already at the strip's right end, showing an icon and raising an action on a press.
    protected Button AddCornerButton(Geometry icon, Action pressed)
    {
        var iconShape = new IconShape { Data = icon };
        iconShape.Bind(Shape.FillProperty, iconShape.GetResourceObservable(ThemeResource.TextSecondaryBrushKey));
        var button = new Button { Content = iconShape };
        button.Classes.Add(ThemeClasses.PanelCorner);
        button.Click += (_, _) => pressed();
        _cornerButtons.Children.Insert(0, button);
        return button;
    }
}
