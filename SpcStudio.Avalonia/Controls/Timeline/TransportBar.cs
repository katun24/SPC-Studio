using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The bar across the foot of the main window and of the SPC Player: the volume, speed and transpose sliders at the left, the transport buttons
// centered, and the position slider and the playback timer at the right.
public sealed class TransportBar : Border
{
    private const double PositionSliderWidth = 480;
    private const double MaxSpeedDoublings = 2;
    private const double SongSpeedDoublings = 0;
    private const double MaxTransposeSemitones = 12;
    private const double NoTransposeSemitones = 0;
    private const int TransposeDecimals = 2;
    private const string TransposeFormat = "0.##";
    private const string PlusSign = "+";
    private const double MinMarginMove = 0.5;
    private const string BarBrushResource = "PanelBrush";

    private readonly StackPanel _playbackSliders;
    private readonly StackPanel _positionAndTimer;
    private readonly PlaybackSlider _volume = new() { Icon = AppIcons.Volume };
    private readonly PlaybackSlider _speed = new() { Icon = AppIcons.Metronome };
    private readonly PlaybackSlider _transpose = new() { Icon = AppIcons.Sharp };
    private readonly Slider _positionSlider = new() { Width = PositionSliderWidth, Minimum = 0, Maximum = 1, IsSnapToTickEnabled = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _timeReadout = new() { VerticalAlignment = VerticalAlignment.Center, FontFeatures = AppFonts.Tabular };
    private bool _isPositionHeld;
    private bool _isWritingPosition;
    private bool _isResetting;

    // Lays the three parts over each other on the panel's surface, gives the three sliders their ranges and puts them where they start.
    public TransportBar()
    {
        Background = ThemeResource.Of<IBrush>(BarBrushResource);
        Padding = new Thickness(Space.Px10, Space.Px6);

        _volume.Slider.Minimum = 0;
        _volume.Slider.Maximum = Percent.PerWhole;
        _speed.Slider.Minimum = -MaxSpeedDoublings;
        _speed.Slider.Maximum = MaxSpeedDoublings;
        _transpose.Slider.Minimum = -MaxTransposeSemitones;
        _transpose.Slider.Maximum = MaxTransposeSemitones;

        _playbackSliders = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = Space.Px24,
            Children = { _volume, _speed, _transpose },
        };

        foreach (var playbackSlider in new[] { _volume, _speed, _transpose })
        {
            playbackSlider.Slider.ValueChanged += (_, _) =>
            {
                if (!_isResetting)
                {
                    ShowAndReportPlayback();
                }
            };
        }

        FieldResets.ResetsTo(_volume.Slider, () => Percent.PerWhole);
        FieldResets.ResetsTo(_speed.Slider, () => SongSpeedDoublings);
        FieldResets.ResetsTo(_transpose.Slider, () => NoTransposeSemitones);
        FieldResets.ResetsTo(_positionSlider, () => _positionSlider.Minimum);

        _positionSlider.Margin = new Thickness(0, 0, Space.Px8, 0);
        _positionSlider.ValueChanged += (_, _) =>
        {
            if (!_isWritingPosition)
            {
                PositionMoved?.Invoke(_positionSlider.Value);
            }
        };

        _positionSlider.AddHandler(PointerPressedEvent, (_, _) => _isPositionHeld = true, RoutingStrategies.Tunnel);
        _positionSlider.AddHandler(PointerReleasedEvent, (_, _) => _isPositionHeld = false, RoutingStrategies.Tunnel);
        _positionSlider.PointerCaptureLost += (_, _) => _isPositionHeld = false;

        _positionAndTimer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _positionSlider, _timeReadout },
        };

        Child = new Panel { Children = { _playbackSliders, Buttons, _positionAndTimer } };

        ResetPlayback();
    }

    // The play/pause and stop buttons.
    public TransportButtons Buttons { get; } = new();

    // Raised by a press on the play/pause button.
    public event EventHandler<RoutedEventArgs>? PlayPauseClick
    {
        add
        {
            Buttons.PlayPauseClick += value;
        }
        remove
        {
            Buttons.PlayPauseClick -= value;
        }
    }

    // Raised by a press on the stop button.
    public event EventHandler<RoutedEventArgs>? StopClick
    {
        add
        {
            Buttons.StopClick += value;
        }
        remove
        {
            Buttons.StopClick -= value;
        }
    }

    // Raised whenever one of the three playback sliders moves.
    public event Action? PlaybackChanged;

    // Raised by a move of the position slider by hand, with the value it moved to in the window's own unit.
    public event Action<double>? PositionMoved;

    // The playback volume, as a gain between silence and one.
    public double VolumeGain
    {
        get
        {
            return _volume.Slider.Value / Percent.PerWhole;
        }
    }

    // The playback speed, as a multiple of the song's own.
    public double SpeedMultiplier
    {
        get
        {
            return Math.Pow(2, _speed.Slider.Value);
        }
    }

    // The playback transpose, in semitones and fractions of one.
    public double TransposeSemitones
    {
        get
        {
            return _transpose.Slider.Value;
        }
    }

    // Puts the three playback sliders back where they start: full volume, the song's own speed and no transpose.
    public void ResetPlayback()
    {
        FlagGuard.RunWithFlagSet(ref _isResetting, () =>
        {
            _volume.Slider.Value = Percent.PerWhole;
            _speed.Slider.Value = SongSpeedDoublings;
            _transpose.Slider.Value = NoTransposeSemitones;
        });

        ShowAndReportPlayback();
    }

    // Writes the timer and, unless it is being held, puts the position slider at a value on a rail running to a maximum, both in the window's unit.
    public void ShowPosition(double value, double maximum, double seconds, double totalSeconds)
    {
        _timeReadout.Text = $"{ClockText(seconds)} / {ClockText(totalSeconds)}";

        if (_isPositionHeld)
        {
            return;
        }

        FlagGuard.RunWithFlagSet(ref _isWritingPosition, () =>
        {
            _positionSlider.Maximum = maximum;
            _positionSlider.Value = SliderField.HeldToRail(_positionSlider, value);
        });
    }

    // Starts the playback sliders level with one control's left edge and ends the timer level with another's right edge, or the panels' inset.
    public void AlignEnds(Visual left, Visual? right)
    {
        if (Child is not Visual row)
        {
            return;
        }

        if (left.TranslatePoint(default, row) is Point start && Math.Abs(_playbackSliders.Margin.Left - start.X) >= MinMarginMove)
        {
            _playbackSliders.Margin = new Thickness(start.X, 0, 0, 0);
        }

        // How far in from the bar's right edge the timer ends: level with the right control, or the panels' inset where there is none.
        double rightInset = right?.TranslatePoint(new Point(right.Bounds.Width, 0), row) is Point end
            ? row.Bounds.Width - end.X
            : Space.Px8 - Padding.Right;

        if (Math.Abs(_positionAndTimer.Margin.Right - rightInset) >= MinMarginMove)
        {
            _positionAndTimer.Margin = new Thickness(0, 0, rightInset, 0);
        }
    }

    // Writes the three readouts and tells the window the values moved.
    private void ShowAndReportPlayback()
    {
        _volume.Readout.Text = Percent.Text(_volume.Slider.Value);
        _speed.Readout.Text = Percent.Text(SpeedMultiplier * Percent.PerWhole);
        _transpose.Readout.Text = TransposeText(Math.Round(TransposeSemitones, TransposeDecimals));

        PlaybackChanged?.Invoke();
    }

    // A rounded transpose as the readout writes it: signed wherever it is not zero.
    private static string TransposeText(double semitones)
    {
        if (semitones == 0)
        {
            return "0";
        }

        return (semitones > 0 ? PlusSign : "") + semitones.ToString(TransposeFormat, CultureInfo.InvariantCulture);
    }

    // Seconds as the timer writes them: minutes, seconds and milliseconds.
    private static string ClockText(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(seconds, 0));
        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}:{time.Milliseconds:000}";
    }
}
