using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpcStudio.Controls;

// One tick under a slider's rail at a value the slider does not hold, drawn where the handle's center would stand for it.
public sealed class SliderMark : SliderOverlay
{
    private const double MarkWidth = 1;

    // The value to mark, set on the slider itself; not a number is no mark at all.
    public static readonly AttachedProperty<double> AtProperty = AvaloniaProperty.RegisterAttached<SliderMark, Slider, double>("At", double.NaN);

    // The value to mark, handed over from the slider through its template.
    public static readonly StyledProperty<double> MarkedValueProperty = AvaloniaProperty.Register<SliderMark, double>(nameof(MarkedValue), double.NaN);

    // Redraws whenever the marked value changes.
    static SliderMark()
    {
        AffectsRender<SliderMark>(MarkedValueProperty);
    }

    // Starts hidden, there being no value to mark yet.
    public SliderMark()
    {
        IsVisible = false;
    }

    // The value to mark, handed over from the slider through its template.
    public double MarkedValue
    {
        get
        {
            return GetValue(MarkedValueProperty);
        }
        set
        {
            SetValue(MarkedValueProperty, value);
        }
    }

    // The value a slider is marked at.
    public static double GetAt(Slider slider)
    {
        return slider.GetValue(AtProperty);
    }

    // Marks a slider at a value, or takes its mark away with not a number.
    public static void SetAt(Slider slider, double at)
    {
        slider.SetValue(AtProperty, at);
    }

    // Hides the mark while there is no value to mark.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkedValueProperty)
        {
            IsVisible = !double.IsNaN(MarkedValue);
        }
    }

    // Draws the tick down the whole height of the control.
    public override void Render(DrawingContext dc)
    {
        if (double.IsNaN(MarkedValue) || Fill is not { } fill || !HasRange)
        {
            return;
        }

        dc.DrawRectangle(fill, null, new Rect(ThumbCenterAt(MarkedValue), 0, MarkWidth, Bounds.Height));
    }
}
