namespace SpcStudio.Controls;

// One axis of what a viewer shows of a surface drawn across the song - the roll, a lane or the ruler: where the view starts along it and how long it is,
// as the viewer last said.
public sealed class ViewSpan
{
    private double _start;
    private double _length;

    // Whether the viewer has said how long the view is, which it has not before its first layout.
    public bool HasLength
    {
        get
        {
            return _length > 0;
        }
    }

    // Whether a start or a length the viewer reports has moved from the ones held by more than the viewport's tolerance.
    public bool Differs(double start, double length)
    {
        return !RollMetrics.IsSameViewportValue(_start, start) || !RollMetrics.IsSameViewportValue(_length, length);
    }

    // Holds a start and a length the viewer reports.
    public void Set(double start, double length)
    {
        _start = start;
        _length = length;
    }

    // Holds a start and a length where they have moved from the ones held, and answers whether they had.
    public bool MoveTo(double start, double length)
    {
        if (!Differs(start, length))
        {
            return false;
        }

        Set(start, length);
        return true;
    }

    // The stretch of a surface the view shows, or the whole surface where the viewer has not said yet.
    public (double Start, double End) VisibleRange(double surfaceLength)
    {
        return HasLength ? (_start, _start + _length) : (0, surfaceLength);
    }
}
