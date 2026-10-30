using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// The loop, end and playback markers, laid over the ruler, the roll and the lane stack together so a marker in view is drawn whole over the keys, the
// lane labels and the ruler's corner beside it, and never taking a press.
public sealed class MarkerLayer : MarkerOverlay
{
    public static readonly StyledProperty<double?> PlayheadStepProperty = AvaloniaProperty.Register<MarkerLayer, double?>(nameof(PlayheadStep));
    public static readonly StyledProperty<double?> AuditionStepProperty = AvaloniaProperty.Register<MarkerLayer, double?>(nameof(AuditionStep));
    private Song? _song;
    private ScrollViewer? _rulerScroll;
    private ScrollViewer? _rollScroll;
    private ScrollViewer? _laneScroll;

    // Redraws the markers whenever the playback position or the audition's moves.
    static MarkerLayer()
    {
        AffectsRender<MarkerLayer>(PlayheadStepProperty, AuditionStepProperty);
    }

    // The song whose loop and end markers are drawn; setting it redraws the layer.
    public Song? Song
    {
        get
        {
            return _song;
        }
        set
        {
            _song = value;
            InvalidateVisual();
        }
    }

    // The playback position in steps, as the roll takes it; null hides the marker.
    public double? PlayheadStep
    {
        get
        {
            return GetValue(PlayheadStepProperty);
        }
        set
        {
            SetValue(PlayheadStepProperty, value);
        }
    }

    // Where the note panel's run has got to in steps, as the roll takes it; null hides the marker.
    public double? AuditionStep
    {
        get
        {
            return GetValue(AuditionStepProperty);
        }
        set
        {
            SetValue(AuditionStepProperty, value);
        }
    }

    // Takes the viewers of the ruler, the roll and the lane stack, whose views the markers are drawn over.
    public void ShowOver(ScrollViewer rulerScroll, ScrollViewer rollScroll, ScrollViewer laneScroll)
    {
        _rulerScroll = rulerScroll;
        _rollScroll = rollScroll;
        _laneScroll = laneScroll;
        InvalidateVisual();
    }

    // Draws the loop marker, the end marker, the playback marker and the audition marker, each one whose position the roll's view shows.
    public override void Render(DrawingContext dc)
    {
        if (_song is null || _rollScroll is null || TopLeftOf(_rollScroll) is not Point rollTopLeft)
        {
            return;
        }

        double gridWidth = RollMetrics.GridWidth(_song);
        double rollBottom = rollTopLeft.Y + Math.Min(_rollScroll.Viewport.Height, RollMetrics.GridHeight - _rollScroll.Offset.Y);
        double endX = RollMetrics.EndMarkerX(_song, gridWidth);
        double? rulerTop = RulerTop();
        using (dc.PushClip(new Rect(0, 0, rollTopLeft.X + _rollScroll.Viewport.Width, Bounds.Height)))
        using (dc.PushTransform(Matrix.CreateTranslation(rollTopLeft.X - _rollScroll.Offset.X, 0)))
        {
            DrawMarkerInView(dc, MarkerLook.OfLoop(_song.IsLooping), RollMetrics.XOfTick(_song.LoopTick), doesFlagPointRight: true, rulerTop, rollTopLeft.Y, rollBottom);
            DrawMarkerInView(dc, MarkerLook.OfEnd(_song.IsEndEnabled), endX, TimeRuler.DoesEndFlagPointRight(endX), rulerTop, rollTopLeft.Y, rollBottom);
            DrawPlayheadInView(dc, MarkerLook.Playhead, PlayheadStep, gridWidth, rulerTop, rollTopLeft.Y, rollBottom);
            DrawPlayheadInView(dc, MarkerLook.Audition, AuditionStep, gridWidth, rulerTop, rollTopLeft.Y, rollBottom);
        }
    }

    // Where the top of the ruler's band stands in the layer, or null where it cannot be placed.
    private double? RulerTop()
    {
        return _rulerScroll is null ? null : TopLeftOf(_rulerScroll)?.Y;
    }

    // Whether a position on the grid is inside the roll's view.
    private bool IsInRollView(double x)
    {
        return IsInView(x, _rollScroll!.Offset.X, _rollScroll.Viewport.Width);
    }

    // Draws a loop or end marker's line down the roll and its flag in the ruler, where its position is in view.
    private void DrawMarkerInView(DrawingContext dc, MarkerLook look, double x, bool doesFlagPointRight, double? rulerTop, double rollTop, double rollBottom)
    {
        if (IsInRollView(x))
        {
            DrawFlagMarker(dc, look, x, doesFlagPointRight, rulerTop, rollTop, rollBottom);
        }
    }

    // Draws a marker shaped as the playback marker at a position in steps - its line down the roll and the lane stack and its head in the ruler - where there is a position and it is in view.
    private void DrawPlayheadInView(DrawingContext dc, MarkerLook look, double? step, double gridWidth, double? rulerTop, double rollTop, double rollBottom)
    {
        if (RollMetrics.PlayheadX(step, gridWidth) is not double x || !IsInRollView(x))
        {
            return;
        }

        DrawPlayhead(dc, look, x, rulerTop, rollTop, rollBottom);
        if (_laneScroll is { IsEffectivelyVisible: true } && TopLeftOf(_laneScroll) is Point laneTopLeft)
        {
            DrawMarkerLine(dc, look, x, laneTopLeft.Y, laneTopLeft.Y + _laneScroll.Viewport.Height);
        }
    }
}
