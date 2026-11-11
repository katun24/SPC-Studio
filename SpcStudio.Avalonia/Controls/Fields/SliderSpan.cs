using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// What a slider draws in place of its handle where what it stands over has no one value: a bar from the lowest value to the highest, with a tick at each one between.
public sealed class SliderSpan : SliderOverlay
{
    private const double PenWidth = 1;

    // The values the slider stands over, set on the slider itself.
    public static readonly AttachedProperty<IReadOnlyList<int>?> ValuesProperty = AvaloniaProperty.RegisterAttached<SliderSpan, Slider, IReadOnlyList<int>?>("Values");

    // The values the slider stands over, handed over from the slider through its template.
    public static readonly StyledProperty<IReadOnlyList<int>?> SpannedValuesProperty = AvaloniaProperty.Register<SliderSpan, IReadOnlyList<int>?>(nameof(SpannedValues));

    // The height of the slider's handle, which the bar is drawn at.
    public static readonly StyledProperty<double> ThumbHeightProperty = AvaloniaProperty.Register<SliderSpan, double>(nameof(ThumbHeight));

    // The brush the bar's border is drawn with.
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<SliderSpan, IBrush?>(nameof(Stroke));

    // The brush the ticks inside the bar are drawn with.
    public static readonly StyledProperty<IBrush?> TickBrushProperty = AvaloniaProperty.Register<SliderSpan, IBrush?>(nameof(TickBrush));

    // Redraws whenever one of the bar's own properties changes.
    static SliderSpan()
    {
        AffectsRender<SliderSpan>(SpannedValuesProperty, ThumbHeightProperty, StrokeProperty, TickBrushProperty);
    }

    // The values the slider stands over, handed over from the slider through its template.
    public IReadOnlyList<int>? SpannedValues
    {
        get
        {
            return GetValue(SpannedValuesProperty);
        }
        set
        {
            SetValue(SpannedValuesProperty, value);
        }
    }

    // The height of the slider's handle, which the bar is drawn at.
    public double ThumbHeight
    {
        get
        {
            return GetValue(ThumbHeightProperty);
        }
        set
        {
            SetValue(ThumbHeightProperty, value);
        }
    }

    // The brush the bar's border is drawn with.
    public IBrush? Stroke
    {
        get
        {
            return GetValue(StrokeProperty);
        }
        set
        {
            SetValue(StrokeProperty, value);
        }
    }

    // The brush the ticks inside the bar are drawn with.
    public IBrush? TickBrush
    {
        get
        {
            return GetValue(TickBrushProperty);
        }
        set
        {
            SetValue(TickBrushProperty, value);
        }
    }

    // The values a slider stands over.
    public static IReadOnlyList<int>? GetValues(Slider slider)
    {
        return slider.GetValue(ValuesProperty);
    }

    // Puts the values a slider stands over on it; null, empty or one distinct value draws nothing.
    public static void SetValues(Slider slider, IReadOnlyList<int>? values)
    {
        slider.SetValue(ValuesProperty, values);
    }

    // Draws the bar and the ticks inside it, where the values disagree and the slider has a range.
    public override void Render(DrawingContext dc)
    {
        if (SpannedValues is not { Count: > 0 } values || !HasRange)
        {
            return;
        }

        int lowest = values.Min();
        int highest = values.Max();
        if (lowest == highest)
        {
            return;
        }

        double top = Math.Round((Bounds.Height - ThumbHeight) / 2);
        double left = ThumbLeftAt(lowest);
        double width = ThumbLeftAt(highest) - left + ThumbWidth - PenWidth;
        var barRect = new Rect(left + DrawnSurface.HalfPixel, top + DrawnSurface.HalfPixel, width, ThumbHeight - PenWidth);
        dc.DrawRectangle(Fill, Stroke is null ? null : new Pen(Stroke, PenWidth), barRect, Rounding.Radius, Rounding.Radius);

        if (TickBrush is not { } tickBrush)
        {
            return;
        }

        var tickPen = new Pen(tickBrush, PenWidth);
        foreach (int value in values.Distinct())
        {
            if (value <= lowest || value >= highest)
            {
                continue;
            }

            double x = ThumbCenterAt(value) + DrawnSurface.HalfPixel;
            dc.DrawLine(tickPen, new Point(x, top + Space.Px3), new Point(x, top + ThumbHeight - Space.Px3));
        }
    }
}
