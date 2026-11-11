using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A vertical strip of every hue its scale draws, the lowest at the top, with a bar across it at the hue picked; a press or a drag picks a hue, and the arrow keys and the wheel step it.
public sealed class HueStrip : Control
{
    private const double StripWidth = 24;
    private const double StripHeight = 225;
    private const double MarkerThickness = 1.6;
    private const double MarkerOverhang = 1;
    private const double MarkerHeight = 3;

    private static readonly Pen MarkerPen = new Pen(new SolidColorBrush(Palette.White), MarkerThickness);

    private readonly Pen _outlinePen = new(ThemeResource.BorderBrush, ThemeResource.FrameStroke);
    private int _hue;
    private bool _isDragging;
    private HueScale _scale = HueScale.InstrumentHues;

    // The ramp the strip is drawn with, in its scale's colors.
    private IBrush _topToBottomHueRampBrush = BuildHueRamp(HueScale.InstrumentHues);

    // Makes the strip take the focus and hangs the arrow keys and the wheel on it.
    public HueStrip()
    {
        Focusable = true;
        FieldSteps.OnKeys(this, OnStepKey);
    }

    // Raised whenever the user has moved the hue, every move of a drag included.
    public event Action? HueChanged;

    // The color of the hue picked; setting the one already showing changes nothing, and setting another moves the bar to its hue and raises nothing.
    public int ColorRgb
    {
        get
        {
            return _scale.ColorOfHue(_hue);
        }
        set
        {
            if (value == ColorRgb)
            {
                return;
            }

            _hue = _scale.HueOf(value) ?? 0;
            InvalidateVisual();
        }
    }

    // The whole hue picked.
    public int Hue
    {
        get
        {
            return _hue;
        }
    }

    // How the strip's hues map to colors, an instrument's unless set otherwise; setting it redraws the ramp in its colors.
    public HueScale Scale
    {
        get
        {
            return _scale;
        }
        set
        {
            _scale = value;
            _topToBottomHueRampBrush = BuildHueRamp(value);
            InvalidateVisual();
        }
    }

    // The strip at its own size.
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(StripWidth, StripHeight);
    }

    // Starts a drag on a left press, taking the focus and moving the bar to the press.
    protected override void OnPointerPressed(PointerPressedEventArgs pressArgs)
    {
        base.OnPointerPressed(pressArgs);
        if (!pressArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDragging = true;
        pressArgs.Pointer.Capture(this);
        Focus();
        MoveHueTo(HueAtHeight(pressArgs.GetPosition(this).Y));
        pressArgs.Handled = true;
    }

    // Moves the bar with the pointer while a drag lasts.
    protected override void OnPointerMoved(PointerEventArgs moveArgs)
    {
        base.OnPointerMoved(moveArgs);
        if (_isDragging)
        {
            MoveHueTo(HueAtHeight(moveArgs.GetPosition(this).Y));
        }
    }

    // Ends the drag and lets the pointer go.
    protected override void OnPointerReleased(PointerReleasedEventArgs releaseArgs)
    {
        base.OnPointerReleased(releaseArgs);
        _isDragging = false;
        releaseArgs.Pointer.Capture(null);
    }

    // Draws the hues with the bar across them at the hue picked.
    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(_topToBottomHueRampBrush, _outlinePen, new Rect(0, 0, StripWidth, StripHeight));

        double markerCenterY = FractionDownStripOf(_hue) * StripHeight;
        context.DrawRectangle(null, MarkerPen, new Rect(-MarkerOverhang, markerCenterY - MarkerHeight / 2, StripWidth + 2 * MarkerOverhang, MarkerHeight));
    }

    // Steps the hue by one the way the arrow points, the wheel's notches arriving as arrows.
    private void OnStepKey(object? sender, KeyEventArgs e)
    {
        if (!FieldSteps.TryTakeStep(e, out int step))
        {
            return;
        }

        MoveHueTo(_hue - step);
    }

    // Moves the bar to a hue, held to the strip's ends, and raises the change where it moved.
    private void MoveHueTo(int hue)
    {
        int heldHue = HueCircle.HueRange.Clamp(hue);
        if (heldHue == _hue)
        {
            return;
        }

        _hue = heldHue;
        InvalidateVisual();
        HueChanged?.Invoke();
    }

    // The whole hue at a height down the strip, held to its ends.
    private static int HueAtHeight(double y)
    {
        return (int)Math.Round(Math.Clamp(y / StripHeight, 0, 1) * HueCircle.MaxHue);
    }

    // How far down the strip a hue stands, from nothing at the top to one at the bottom.
    private static double FractionDownStripOf(int hue)
    {
        return hue / (double)HueCircle.MaxHue;
    }

    // A ramp down the strip with a stop at every whole hue, in a scale's colors.
    private static IBrush BuildHueRamp(HueScale scale)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        };
        for (int hue = 0; hue <= HueCircle.MaxHue; hue++)
        {
            brush.GradientStops.Add(new GradientStop(ChannelPalette.FromPackedRgb(scale.ColorOfHue(hue)), FractionDownStripOf(hue)));
        }

        return brush;
    }
}
