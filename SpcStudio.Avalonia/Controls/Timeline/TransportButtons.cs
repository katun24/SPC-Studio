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

    // Lays the two buttons out side by side, the play/pause one showing play.
    public TransportButtons()
    {
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;

        var glyphBrush = ThemeResource.TextPrimaryBrush;
        _playPauseGlyph = NewGlyph(glyphBrush, TransportIcons.Play);

        var playPauseButton = NewTransportButton(_playPauseGlyph);
        playPauseButton.Click += (_, e) => PlayPauseClick?.Invoke(this, e);

        var stopButton = NewTransportButton(NewGlyph(glyphBrush, TransportIcons.Stop));
        stopButton.Margin = new Thickness(Space.Px12, 0, 0, 0);
        stopButton.Click += (_, e) => StopClick?.Invoke(this, e);

        Children.Add(playPauseButton);
        Children.Add(stopButton);
    }

    // Raised by a press on the play/pause button.
    public event EventHandler<RoutedEventArgs>? PlayPauseClick;

    // Raised by a press on the stop button.
    public event EventHandler<RoutedEventArgs>? StopClick;

    // Shows the play glyph, which is what the button does next while nothing plays.
    public void ShowPlay()
    {
        _playPauseGlyph.Data = TransportIcons.Play;
    }

    // Shows the loop glyph, which is what the button does next while nothing plays and Shift is held.
    public void ShowLoop()
    {
        _playPauseGlyph.Data = TransportIcons.Loop;
    }

    // Shows the pause glyph, which is what the button does next while playing.
    public void ShowPause()
    {
        _playPauseGlyph.Data = TransportIcons.Pause;
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
