using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpcStudio.Controls;

// The playback and loop markers of one drawn surface, laid over it and the ruler above it, each in view drawn whole past the view's edges.
public sealed class SurfaceMarkerLayer : MarkerOverlay
{
    private MarkedSurface? _surface;
    private Visual? _rulerBand;
    private Control? _view;

    // Takes the surface the markers stand on, the band their flags and heads hang in, and the control whose bounds are the view onto the surface - a
    // scroll viewer's being its viewport - redrawing whenever a marker moves or the view scrolls.
    public void ShowOver(MarkedSurface surface, Visual rulerBand, Control view)
    {
        _surface = surface;
        _rulerBand = rulerBand;
        _view = view;
        surface.MarkersMoved += InvalidateVisual;
        if (view is ScrollViewer viewer)
        {
            viewer.ScrollChanged += (_, _) => InvalidateVisual();
        }

        InvalidateVisual();
    }

    // Draws the loop marker and the playback marker, each whose position the view shows.
    public override void Render(DrawingContext dc)
    {
        if (_surface is null || _view is null || TopLeftOf(_surface) is not Point surfaceTopLeft || TopLeftOf(_view) is not Point viewTopLeft)
        {
            return;
        }

        var viewSize = VisibleSizeOf(_view);
        double viewLeft = viewTopLeft.X - surfaceTopLeft.X;
        double lineTop = surfaceTopLeft.Y + _surface.MarkerLineTop;
        double lineBottom = Math.Min(surfaceTopLeft.Y + _surface.Bounds.Height, viewTopLeft.Y + viewSize.Height);
        double? rulerTop = _rulerBand is null ? null : TopLeftOf(_rulerBand)?.Y;
        using (dc.PushTransform(Matrix.CreateTranslation(surfaceTopLeft.X, 0)))
        {
            if (_surface.LoopMarkerX is double loopX && IsInView(loopX, viewLeft, viewSize.Width))
            {
                DrawFlagMarker(dc, MarkerLook.OfLoop(_surface.IsLoopMarkerOn), loopX, doesFlagPointRight: true, rulerTop, lineTop, lineBottom);
            }

            if (_surface.PlayheadX is double playheadX && IsInView(playheadX, viewLeft, viewSize.Width))
            {
                DrawPlayhead(dc, MarkerLook.Playhead, playheadX, rulerTop, lineTop, lineBottom);
            }
        }
    }

    // How much of the surface a view shows: a scroll viewer's viewport, or a plain control's own size.
    private static Size VisibleSizeOf(Control view)
    {
        return view is ScrollViewer viewer ? viewer.Viewport : view.Bounds.Size;
    }
}
