using Avalonia.Controls;

namespace SpcStudio.Controls;

// A drawn surface whose playback and loop markers a surface marker layer draws over it: where they stand, and when they move.
public abstract class MarkedSurface : Control
{
    // Raised whenever a marker moves, or the surface under it changes where one stands.
    public event Action? MarkersMoved;

    // Where the playback marker stands across the surface, or null where none is drawn.
    public abstract double? PlayheadX { get; }

    // Where the loop marker stands across the surface, or null where none is drawn.
    public abstract double? LoopMarkerX { get; }

    // Whether the loop marker is drawn on rather than dimmed.
    public abstract bool IsLoopMarkerOn { get; }

    // How far down the surface the markers' lines start.
    public abstract double MarkerLineTop { get; }

    // Tells the marker layer the markers moved.
    protected void ReportMarkersMoved()
    {
        MarkersMoved?.Invoke();
    }

    // Redraws the surface and tells the marker layer it was redrawn.
    protected void Redraw()
    {
        InvalidateVisual();
        ReportMarkersMoved();
    }
}
