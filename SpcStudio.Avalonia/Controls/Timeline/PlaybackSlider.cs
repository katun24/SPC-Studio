using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// One of the transport bar's playback controls - the volume, the speed or the transpose: an icon, a slider and the value it is at, each at a fixed width.
public sealed class PlaybackSlider : Grid
{
    private const double SliderWidth = 110;
    private const double ReadoutWidth = 48;
    private const string ColumnLayout = "Auto,Auto,Auto";
    private const int SliderColumn = 1;
    private const int ReadoutColumn = 2;

    private readonly IconShape _iconShape = new() { VerticalAlignment = VerticalAlignment.Center };

    // The glyph in front of the slider.
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<PlaybackSlider, Geometry?>(nameof(Icon));

    // Lays the icon, the slider and the readout out in a row, the slider spaced off both by the theme's gap.
    public PlaybackSlider()
    {
        ColumnDefinitions = new ColumnDefinitions(ColumnLayout);
        VerticalAlignment = VerticalAlignment.Center;

        _iconShape.Fill = ThemeResource.TextSecondaryBrush;
        Slider.Margin = new Thickness(Space.Px8, 0, Space.Px8, 0);

        SetColumn(Slider, SliderColumn);
        SetColumn(Readout, ReadoutColumn);

        Children.Add(_iconShape);
        Children.Add(Slider);
        Children.Add(Readout);
    }

    // The glyph in front of the slider.
    public Geometry? Icon
    {
        get
        {
            return GetValue(IconProperty);
        }
        set
        {
            SetValue(IconProperty, value);
        }
    }

    // The slider itself, whose range and value the window sets.
    public Slider Slider { get; } = new() { Width = SliderWidth, IsSnapToTickEnabled = false, VerticalAlignment = VerticalAlignment.Center };

    // The value as the window writes it out, in figures of one width.
    public TextBlock Readout { get; } = new() { Width = ReadoutWidth, VerticalAlignment = VerticalAlignment.Center, FontFeatures = AppFonts.Tabular };

    // Draws the new glyph when the icon is set.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IconProperty)
        {
            _iconShape.Data = Icon;
        }
    }
}
