using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A square button filled with a color, which opens a hue strip beside itself on a press and shuts it on another press or on Escape; moving the strip's
// bar is what changes the hue.
public sealed class HueButton : Panel
{
    private readonly Button _swatchButton = new() { Classes = { ThemeClasses.SquareIcon } };
    private readonly HueStrip _strip = new();
    private readonly Popup _stripPopup;

    // Builds the button and the popup holding the strip, the strip taking the focus whenever the popup opens.
    public HueButton()
    {
        _stripPopup = new Popup
        {
            Child = _strip,
            Placement = PlacementMode.RightEdgeAlignedTop,
            IsLightDismissEnabled = true,
            PlacementTarget = _swatchButton,
            HorizontalOffset = Space.Px4,
        };
        _swatchButton.Click += (_, _) => _stripPopup.IsOpen = !_stripPopup.IsOpen;
        _stripPopup.Opened += (_, _) => _strip.Focus();
        _strip.HueChanged += () => HueChanged?.Invoke(this);
        _strip.KeyDown += OnStripKeyDown;
        Children.Add(_swatchButton);
        Children.Add(_stripPopup);
    }

    // Raised with the button whenever the user has moved the strip's bar, every move of a drag included.
    public event Action<HueButton>? HueChanged;

    // The color of the hue picked; setting it fills the button with it and moves the strip's bar to its hue, as the strip's own does.
    public int ColorRgb
    {
        get
        {
            return _strip.ColorRgb;
        }
        set
        {
            _swatchButton.Background = new SolidColorBrush(ChannelPalette.FromPackedRgb(value));
            _strip.ColorRgb = value;
        }
    }

    // The whole hue picked.
    public int Hue
    {
        get
        {
            return _strip.Hue;
        }
    }

    // How the strip's hues map to colors, an instrument's unless set otherwise.
    public HueScale Scale
    {
        get
        {
            return _strip.Scale;
        }
        set
        {
            _strip.Scale = value;
        }
    }

    // The button's tooltip.
    public string? Tooltip
    {
        get
        {
            return ToolTip.GetTip(_swatchButton) as string;
        }
        set
        {
            ToolTip.SetTip(_swatchButton, value);
        }
    }

    // Shuts the strip on Escape.
    private void OnStripKeyDown(object? sender, KeyEventArgs e)
    {
        if (!WindowKeys.IsBareEscape(e))
        {
            return;
        }

        _stripPopup.IsOpen = false;
        e.Handled = true;
    }
}
