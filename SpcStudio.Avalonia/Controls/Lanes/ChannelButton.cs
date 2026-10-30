using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// One channel's button: its color, its mute and solo toggles, a delete button on an extra channel, and its number, filled with the color while it
// is the channel selected - or the same button carrying a word or an icon instead.
public sealed class ChannelButton
{
    private const double ButtonWidth = 108;
    private const double ButtonHeight = 48;
    private const double ButtonFontSize = 14;
    private const double ButtonBorderThickness = 2;
    private const double NumberWidth = 22;
    private const double UnselectedFillDimming = 0.85;
    private const double IdleToggleDimming = 0.45;

    private readonly Color _color;
    private readonly MuteSoloPair? _toggles;
    private readonly Button? _delete;
    private readonly IconShape? _deleteIcon;

    // Builds the button around its content, outlined in its color, reporting a press to the handler given unless it landed on one of its icons.
    private ChannelButton(object content, Color color, Action pressed, MuteSoloPair? toggles = null, Button? delete = null)
    {
        _color = color;
        _toggles = toggles;
        _delete = delete;
        _deleteIcon = delete?.Content as IconShape;
        Button = new Button
        {
            Classes = { ThemeClasses.ChannelButton },
            Content = content,
            Width = ButtonWidth,
            Height = ButtonHeight,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, Space.Px4),
            FontSize = ButtonFontSize,
            FontWeight = FontWeight.SemiBold,
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(ButtonBorderThickness),
        };
        Button.Click += (_, _) =>
        {
            if (!IsPointerOverIcon)
            {
                pressed();
            }
        };
    }

    public Button Button { get; }

    // Whether the pointer is over one of the button's icons, so a press there mutes, solos or deletes without selecting.
    private bool IsPointerOverIcon
    {
        get
        {
            return _toggles?.IsPointerOver == true || _delete?.IsPointerOver == true;
        }
    }

    // A channel's button: its mute and solo toggles with the tooltips given, each reporting the channel and its new state, a delete button on an extra
    // channel where a delete handler is given, and its number at the right, in the channel's own color, reporting a press with its channel.
    public static ChannelButton Create(int channel, string muteTooltip, string soloTooltip, Action<int, bool> muteToggled, Action<int, bool> soloToggled, Action<int> pressed,
        Action<int>? deleted = null)
    {
        var toggles = new MuteSoloPair(channel, muteTooltip, soloTooltip, muteToggled, soloToggled);
        var delete = Song.IsExtraChannel(channel) && deleted is not null ? CreateDeleteButton(() => deleted(channel)) : null;
        if (delete is not null)
        {
            toggles.Panel.Children.Add(delete);
        }

        var number = new TextBlock
        {
            Text = (channel + 1).ToString(),
            Width = NumberWidth,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(number, Dock.Right);
        var content = new DockPanel { Children = { number, toggles.Panel } };

        var button = new ChannelButton(content, ChannelPalette.ColorOfChannel(channel), () => pressed(channel), toggles, delete);
        button.Button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Button.Padding = new Thickness(Space.Px4, 0);
        return button;
    }

    // A button of the same shape carrying a word in one color.
    public static ChannelButton CreateLabeled(string text, Color color, Action pressed)
    {
        return new ChannelButton(new TextBlock { Text = text }, color, pressed);
    }

    // A button of the same shape carrying an icon in one color, the icon drawn in the button's text color.
    public static ChannelButton CreateIconed(Geometry icon, Color color, Action pressed)
    {
        var shape = new IconShape { Data = icon };
        var button = new ChannelButton(shape, color, pressed);
        shape[!Shape.FillProperty] = button.Button[!Button.ForegroundProperty];
        return button;
    }

    // Draws the button selected or not, and its icons in the color that says whether the channel is muted and soloed.
    public void ShowState(bool isSelected, bool isMuted = false, bool isSoloed = false)
    {
        var foreground = isSelected ? Palette.NearBlack : _color;
        Button.Background = new SolidColorBrush(isSelected ? _color : ChannelPalette.Blend(_color, Palette.PanelBackground, UnselectedFillDimming));
        Button.Foreground = new SolidColorBrush(foreground);

        var idleToggleBrush = new SolidColorBrush(ChannelPalette.Blend(foreground, Palette.PanelBackground, IdleToggleDimming));
        _toggles?.ShowState(isMuted, isSoloed, idleToggleBrush);
        if (_deleteIcon is not null)
        {
            _deleteIcon.Fill = idleToggleBrush;
        }
    }

    // The bin beside an extra channel's toggles, reporting a press to the handler given.
    private static Button CreateDeleteButton(Action deleted)
    {
        var button = new Button
        {
            Classes = { ThemeClasses.ChannelButtonIcon },
            Content = new IconShape { Data = AppIcons.Bin },
        };
        button.Click += (_, _) => deleted();
        ToolTip.SetTip(button, Tooltips.DeleteChannel);
        return button;
    }
}
