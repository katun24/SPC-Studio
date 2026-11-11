using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SpcStudio.Theme;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace SpcStudio.Controls;

// The transport's play/pause and stop buttons, which each window starts and stops its own playback with.
public sealed class TransportButtons : StackPanel
{
    private const double ButtonWidth = 68;
    private const double ButtonHeight = 56;

    private readonly ShapePath _playPauseGlyph;
    private readonly Button _playPauseButton;
    private readonly Button _stopButton;
    private string? _playTooltip;
    private string? _pauseTooltip;

    // Lays the two buttons out side by side, the play/pause one showing play.
    public TransportButtons()
    {
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;

        var glyphBrush = ThemeResource.TextPrimaryBrush;
        _playPauseGlyph = NewGlyph(glyphBrush, TransportIcons.Play);

        _playPauseButton = NewTransportButton(_playPauseGlyph);
        _playPauseButton.Click += (_, e) => PlayPauseClick?.Invoke(this, e);

        _stopButton = NewTransportButton(NewGlyph(glyphBrush, TransportIcons.Stop));
        _stopButton.Margin = new Thickness(Space.Px12, 0, 0, 0);
        _stopButton.Click += (_, e) => StopClick?.Invoke(this, e);

        Children.Add(_playPauseButton);
        Children.Add(_stopButton);
    }

    // Raised by a press on the play/pause button.
    public event EventHandler<RoutedEventArgs>? PlayPauseClick;

    // Raised by a press on the stop button.
    public event EventHandler<RoutedEventArgs>? StopClick;

    // Gives the two buttons the tooltips of what the window plays: the play/pause button's two, which follow its glyph, and the stop button's.
    public void UseTooltips(string playTooltip, string pauseTooltip, string stopTooltip)
    {
        _playTooltip = playTooltip;
        _pauseTooltip = pauseTooltip;
        ToolTip.SetTip(_stopButton, stopTooltip);
        ShowPlayPauseFace(_playPauseGlyph.Data);
    }

    // Shows the play glyph, which is what the button does next while nothing plays.
    public void ShowPlay()
    {
        ShowPlayPauseFace(TransportIcons.Play);
    }

    // Shows the loop glyph, which is what the button does next while nothing plays and Shift is held.
    public void ShowLoop()
    {
        ShowPlayPauseFace(TransportIcons.Loop);
    }

    // Shows the pause glyph, which is what the button does next while playing.
    public void ShowPause()
    {
        ShowPlayPauseFace(TransportIcons.Pause);
    }

    // Shows a glyph on the play/pause button with the tooltip that goes with it: the pause tooltip with the pause glyph, and the play tooltip otherwise.
    private void ShowPlayPauseFace(Geometry? glyph)
    {
        _playPauseGlyph.Data = glyph;
        ToolTip.SetTip(_playPauseButton, glyph == TransportIcons.Pause ? _pauseTooltip : _playTooltip);
    }

    // One of the transport's glyphs, drawn at its own size.
    private static ShapePath NewGlyph(IBrush brush, Geometry glyph)
    {
        return new ShapePath { Stretch = Stretch.None, Fill = brush, Data = glyph };
    }

    // One of the two buttons, carrying a glyph.
    private static Button NewTransportButton(Control glyph)
    {
        var button = new Button { Width = ButtonWidth, Height = ButtonHeight, Content = glyph };
        button.Classes.Add(ThemeClasses.Emphasized);
        return button;
    }
}
