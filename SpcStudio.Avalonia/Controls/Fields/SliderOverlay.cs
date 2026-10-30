using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpcStudio.Controls;

// Something drawn over or under a slider's rail at the values a slider stands over, placed where the slider's handle would stand for each of them.
public abstract class SliderOverlay : Control
{
    private const double DefaultMaximum = 1;

    // The lowest value of the slider it is drawn against.
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<SliderOverlay, double>(nameof(Minimum));

    // The highest value of the slider it is drawn against.
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<SliderOverlay, double>(nameof(Maximum), DefaultMaximum);

    // The width of the slider's handle, which the values are laid out against.
    public static readonly StyledProperty<double> ThumbWidthProperty = AvaloniaProperty.Register<SliderOverlay, double>(nameof(ThumbWidth));

    // The brush the overlay is filled with.
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<SliderOverlay, IBrush?>(nameof(Fill));

    // Redraws whenever one of the shared properties changes.
    static SliderOverlay()
    {
        AffectsRender<SliderOverlay>(MinimumProperty, MaximumProperty, ThumbWidthProperty, FillProperty);
    }

    // Leaves every press to the slider under it.
    protected SliderOverlay()
    {
        IsHitTestVisible = false;
    }

    // The lowest value of the slider it is drawn against.
    public double Minimum
    {
        get
        {
            return GetValue(MinimumProperty);
        }
        set
        {
            SetValue(MinimumProperty, value);
        }
    }

    // The highest value of the slider it is drawn against.
    public double Maximum
    {
        get
        {
            return GetValue(MaximumProperty);
        }
        set
        {
            SetValue(MaximumProperty, value);
        }
    }

    // The width of the slider's handle, which the values are laid out against.
    public double ThumbWidth
    {
        get
        {
            return GetValue(ThumbWidthProperty);
        }
        set
        {
            SetValue(ThumbWidthProperty, value);
        }
    }

    // The brush the overlay is filled with.
    public IBrush? Fill
    {
        get
        {
            return GetValue(FillProperty);
        }
        set
        {
            SetValue(FillProperty, value);
        }
    }

    // Whether the slider runs over a range a value can be placed on.
    protected bool HasRange
    {
        get
        {
            return Maximum > Minimum;
        }
    }

    // Where the handle's left edge stands for a value, the value held to the range; only asked where there is a range.
    protected double ThumbLeftAt(double value)
    {
        double travel = Math.Max(0, Bounds.Width - ThumbWidth);
        return (Math.Clamp(value, Minimum, Maximum) - Minimum) / (Maximum - Minimum) * travel;
    }

    // Where the handle's center stands for a value, rounded to a whole pixel.
    protected double ThumbCenterAt(double value)
    {
        return Math.Round(ThumbLeftAt(value) + ThumbWidth / 2);
    }
}
