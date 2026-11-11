using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SpcStudio.Audio;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The bar above the sample editor's waveform: the sample numbered in milliseconds across the stretch the waveform shows, and its presses handed to the waveform.
public sealed class SampleRuler : Control
{
    private const double ViewTolerance = 0.0001;
    private const string UnitLabel = "ms";

    private static readonly double[] LabelStepsMs = [0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000];
    private static readonly RulerScale MsScale = new(LabelStepsMs, Space.Px60, MsLabel);
    private double _offset;
    private double _zoom = 1;
    private double _pointsPerMs = DspPoint.PointsPerMs;

    // Builds the band, clipping what it draws.
    public SampleRuler()
    {
        ClipToBounds = true;
    }

    // Raised with every press, for the waveform to answer in its own coordinates, which are the ruler's.
    public event Action<PointerPressedEventArgs>? Pressed;

    // Puts the ruler on the stretch of the sample the waveform is showing, redrawing only where it moved.
    public void SetView(double offset, double zoom)
    {
        if (Math.Abs(_offset - offset) < ViewTolerance && Math.Abs(_zoom - zoom) < ViewTolerance)
        {
            return;
        }

        _offset = offset;
        _zoom = zoom;
        InvalidateVisual();
    }

    // Numbers the ruler in the time it takes to play this many of the waveform's points a second, redrawing only where it changed.
    public void SetPointsPerSecond(double pointsPerSecond)
    {
        double pointsPerMs = pointsPerSecond / TimeUnits.MsPerSecond;
        if (pointsPerMs == _pointsPerMs)
        {
            return;
        }

        _pointsPerMs = pointsPerMs;
        InvalidateVisual();
    }

    // Draws the background, the unit, and the ticks and labels across the view.
    public override void Render(DrawingContext dc)
    {
        double width = Bounds.Width;
        RulerBand.DrawBackground(dc, width);
        if (width < 1 || _zoom <= 0)
        {
            return;
        }

        var unit = RulerBand.LabelText(UnitLabel);
        RulerBand.DrawLabelRightOf(dc, unit, 0);
        double unitRightEdge = RulerBand.LabelRightEdge(unit, 0) + Space.Px5;
        MsScale.Draw(dc, _zoom * _pointsPerMs, _offset / _pointsPerMs, 0, width, double.PositiveInfinity, unitRightEdge);
    }

    // Asks for the whole width on offer at the band's height.
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(availableSize.Width, RulerBand.Height);
    }

    // Hands the press to the waveform.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Pressed?.Invoke(e);
    }

    // A label: the milliseconds, to as many decimals as the step needs.
    private static string MsLabel(double ms, double stepMs)
    {
        return ms.ToString("F" + DecimalsOfStep(stepMs), CultureInfo.InvariantCulture);
    }

    // How many decimals a label on a step of this many milliseconds needs.
    private static int DecimalsOfStep(double stepMs)
    {
        if (stepMs >= 1)
        {
            return 0;
        }

        return stepMs >= 0.1 ? 1 : 2;
    }
}
