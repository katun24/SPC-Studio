using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SpcStudio.Audio;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A sample's decoded audio drawn as a waveform across the stretch in view, with its selection, where the playback marker and the loop marker stand, and the
// zoom and scroll over them, panned along under a drag held past either edge.
public sealed class WaveformView : MarkedSurface, IScrolledView
{
    private const double MaxPixelsPerPoint = 32;
    private const double MinPixelsPerPointForJoinedPoints = 1;
    private const double MinPixelsPerPointForNodes = 8;
    private const double NodeRadius = 2.5;
    private const double ScrollFractionPerNotch = 0.1;
    private const double WaveLineThickness = 1;
    private const double MinPeakLineLength = 1;
    private const double MinDrawnWidth = 1;
    private const double MinDrawnHeight = 2;
    private const int OffscreenPointsEachSide = 1;
    private const int NoLoopBlockAsked = -1;

    private readonly Pen _centerLinePen = new Pen(new SolidColorBrush(Palette.SlateGray), WaveLineThickness);
    private readonly Pen _wavePen = new Pen(new SolidColorBrush(Palette.SkyBlue), WaveLineThickness);
    private readonly Brush _nodeBrush = new SolidColorBrush(Palette.SkyBlue);
    private readonly Brush _selectionBrush = new SolidColorBrush(Palette.WaveformSelection);
    private readonly WindowCursor _windowCursor = new();
    private readonly EdgeScroll _edgeScroll;
    private Sample? _sample;
    private bool _isDraggingLoop;
    private bool _isDraggingMarker;
    private bool _isSelecting;
    private int _lastLoopBlockAsked = NoLoopBlockAsked;
    private int _markerPoint;
    private int _anchorPoint;
    private int _focusPoint;
    private int? _playPoint;
    private double _pixelsPerPoint = MaxPixelsPerPoint;
    private double _leftmostPoint;
    private bool _isPanning;
    private int? _fittedSampleId;
    private bool _isFitPending;

    // Raised with the block the loop is to start at, as Ctrl and the wheel button or the ruler's loop flag place or drag the loop marker.
    public event Action<int>? LoopBlockDragged;

    // Raised by the four editing keys with the edit they ask for.
    public event Action<SampleEditKind>? EditRequested;

    // Raised whenever the zoom, the scroll or the sample changes what can be scrolled through.
    public event Action? ViewChanged;

    // Raised whenever the marker moves.
    public event Action? MarkerMoved;

    // Raised whenever the selection changes.
    public event Action? SelectionChanged;

    // Makes the control take the focus, clip what it draws, follow its own size and pan under a drag held past an edge.
    public WaveformView()
    {
        _edgeScroll = new EdgeScroll(this, EdgeScroll.UnzoomedRollColumn);
        Focusable = true;
        ClipToBounds = true;
        SizeChanged += (_, _) => FollowNewSize();
    }

    // The sample being drawn, or null for none; a different sample resets the view and the same one keeps it.
    public Sample? Sample
    {
        get
        {
            return _sample;
        }
        set
        {
            _sample = value;
            if (_fittedSampleId != value?.Id)
            {
                _fittedSampleId = value?.Id;
                ResetView();
                return;
            }

            ClampAllAndReportView();
        }
    }

    // The points in the sample, or none where there is no sample.
    public int PointCount
    {
        get
        {
            return _sample?.PointCount ?? 0;
        }
    }

    // The lower end of the selection, as a point boundary.
    public int SelectionStart
    {
        get
        {
            return Math.Min(_anchorPoint, _focusPoint);
        }
    }

    // The upper end of the selection, as a point boundary past its last point.
    public int SelectionEnd
    {
        get
        {
            return Math.Max(_anchorPoint, _focusPoint);
        }
    }

    // How many points the selection covers.
    public int SelectedPointCount
    {
        get
        {
            return SelectionEnd - SelectionStart;
        }
    }

    // Whether the selection covers any point at all.
    public bool HasSelection
    {
        get
        {
            return SelectedPointCount > 0;
        }
    }

    // The marker, as a point boundary.
    public int MarkerPoint
    {
        get
        {
            return _markerPoint;
        }
    }

    // The point a running preview has reached, where the marker is drawn while it runs, or null where none is loaded.
    public int? PlayPoint
    {
        get
        {
            return _playPoint;
        }
        set
        {
            if (_playPoint == value)
            {
                return;
            }

            _playPoint = value;
            ReportMarkersMoved();
        }
    }

    // Where the marker is drawn - at the point a running preview has reached, or on the marker's own point otherwise - or null where there is no wave.
    public override double? PlayheadX
    {
        get
        {
            return HasPoints ? XOfPoint(_playPoint ?? _markerPoint) : null;
        }
    }

    // Where the sample resumes from, or null where there is no wave.
    public override double? LoopMarkerX
    {
        get
        {
            return HasPoints ? XOfPoint(_sample!.Decoded.LoopStartPoint) : null;
        }
    }

    // Whether the sample loops, the loop marker being dimmed where it does not.
    public override bool IsLoopMarkerOn
    {
        get
        {
            return _sample?.Decoded.DoesLoop ?? false;
        }
    }

    // The markers' lines run the waveform's whole height.
    public override double MarkerLineTop
    {
        get
        {
            return 0;
        }
    }

    // How many pixels one point is drawn across, held between the whole sample in view and the closest zoom.
    public double PixelsPerPoint
    {
        get
        {
            return _pixelsPerPoint;
        }
        set
        {
            _pixelsPerPoint = Math.Clamp(value, WholeSamplePixelsPerPoint, MaxPixelsPerPoint);
            ClampScrollAndReportView();
        }
    }

    // The leftmost point drawn, held inside the sample.
    public double LeftmostPoint
    {
        get
        {
            return _leftmostPoint;
        }
        set
        {
            _leftmostPoint = value;
            ClampScrollAndReportView();
        }
    }

    // How many points fit across the view at the current zoom.
    public double ViewportPoints
    {
        get
        {
            return Bounds.Width < MinDrawnWidth ? 0 : Bounds.Width / _pixelsPerPoint;
        }
    }

    // The furthest right the leftmost point drawn can be, with the sample's end at the view's right edge.
    public double FurthestLeftmostPoint
    {
        get
        {
            return Math.Max(0, PointCount - ViewportPoints);
        }
    }

    // Where the view starts across the zoomed sample, in pixels, held inside the sample as it is set.
    public Vector Offset
    {
        get
        {
            return new Vector(_leftmostPoint * _pixelsPerPoint, 0);
        }
        set
        {
            LeftmostPoint = value.X / _pixelsPerPoint;
        }
    }

    // The view's own size.
    public Size Viewport
    {
        get
        {
            return Bounds.Size;
        }
    }

    // The furthest the view can start across the zoomed sample, in pixels.
    public Vector FurthestOffset
    {
        get
        {
            return new Vector(FurthestLeftmostPoint * _pixelsPerPoint, 0);
        }
    }

    // Whether the loop marker, the marker or a selection is being dragged.
    private bool IsDragHeld
    {
        get
        {
            return _isDraggingLoop || _isDraggingMarker || _isSelecting;
        }
    }

    // Whether there is a sample with any points to draw.
    private bool HasPoints
    {
        get
        {
            return PointCount > 0;
        }
    }

    // The blocks in the sample, or none where there is no sample.
    private int BlockCount
    {
        get
        {
            return _sample?.BlockCount ?? 0;
        }
    }

    // The last block the loop can start on, the first where there are none.
    private int LastBlock
    {
        get
        {
            return Math.Max(0, BlockCount - 1);
        }
    }

    // The zoom that fits the whole sample in the view, never closer than the closest zoom.
    private double WholeSamplePixelsPerPoint
    {
        get
        {
            return PointCount == 0 || Bounds.Width < MinDrawnWidth ? MaxPixelsPerPoint : Math.Min(MaxPixelsPerPoint, Bounds.Width / PointCount);
        }
    }

    // Puts the selection on a range of point boundaries, the marker staying where it is.
    public void Select(int anchorPoint, int focusPoint)
    {
        _anchorPoint = anchorPoint;
        _focusPoint = focusPoint;
        ClampViewMarkerAndSelection();
        RedrawAndReportSelection();
    }

    // Selects every point, the marker staying where it is.
    public void SelectAll()
    {
        if (_sample is null)
        {
            return;
        }

        _anchorPoint = 0;
        _focusPoint = PointCount;
        RedrawAndReportSelection();
    }

    // Moves the marker to a point boundary, the selection staying as it is.
    public void MoveMarkerTo(int point)
    {
        if (_sample is null)
        {
            return;
        }

        _markerPoint = point;
        ReportPlaybackMarkerMoved();
    }

    // The block the loop marker stands on at an edge of the sample - its first or its last - or null where there are no points to loop.
    public int? LoopBlockAt(ViewEdge edge)
    {
        return HasPoints ? ViewEdges.ValueAt(edge, 0, LastBlock) : null;
    }

    // Moves the selection's end on a point boundary's side of its middle to that boundary, or selects from the marker to it where nothing is selected.
    public void ResizeSelectionTo(int point)
    {
        if (_sample is null)
        {
            return;
        }

        _anchorPoint = HasSelection ? SelectionEndHeldFrom(point) : _markerPoint;
        MoveSelectionFocusTo(point);
    }

    // Takes a press on the ruler above, whose coordinates are this control's: a left press on the loop marker's flag drags the loop marker, and any other
    // press is answered as it is here.
    public void TakeRulerPress(PointerPressedEventArgs e)
    {
        _windowCursor.Remember(this, e);
        bool isOnLoopFlag = LoopMarkerX is double loopX && RulerBand.IsFlagGrabbed(e.GetPosition(this).X, loopX, doesPointRight: true);
        if (isOnLoopFlag && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            StartLoopDrag(e);
            return;
        }

        TakePress(e);
    }

    // Draws the background, the center line, and over a sample its selection and its wave.
    public override void Render(DrawingContext dc)
    {
        double width = Bounds.Width;
        double height = Bounds.Height;
        dc.DrawRectangle(DrawnSurface.Background, null, new Rect(0, 0, width, height));
        double middle = height / 2;
        dc.DrawLine(_centerLinePen, new Point(0, middle), new Point(width, middle));
        var points = _sample?.Decoded.Points;
        if (points is null || points.Length == 0 || width < MinDrawnWidth || height < MinDrawnHeight)
        {
            return;
        }

        DrawSelection(dc, width, height);
        if (_pixelsPerPoint < MinPixelsPerPointForJoinedPoints)
        {
            DrawColumnPeaks(dc, points, width, middle);
        }
        else
        {
            DrawJoinedPoints(dc, points, middle);
            if (_pixelsPerPoint >= MinPixelsPerPointForNodes)
            {
                DrawNodes(dc, points, middle);
            }
        }
    }

    // Answers a press on the waveform itself.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _windowCursor.Remember(this, e);
        TakePress(e);
    }

    // Carries on whichever drag a press started.
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _windowCursor.Remember(this, e);
        if (_isPanning || !IsDragHeld)
        {
            return;
        }

        FollowDragAt(e.GetPosition(this).X);
    }

    // Lets the pointer go at the end of whichever drag a press started, which ends it.
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (IsDragHeld || _isPanning)
        {
            e.Pointer.Capture(null);
        }
    }

    // Ends whichever drag a press started once the pointer is let go or taken away.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDraggingLoop = false;
        _isDraggingMarker = false;
        _isSelecting = false;
        _isPanning = false;
        _edgeScroll.Stop();
    }

    // Ctrl and the wheel zooms around the point under the cursor, and the wheel alone scrolls, Shift held or not.
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_sample is null)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            ZoomAroundCursor(e);
            return;
        }

        if (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            e.Handled = true;
            LeftmostPoint = _leftmostPoint - e.Delta.Y * ViewportPoints * ScrollFractionPerNotch;
        }
    }

    // Raises the edit a Delete, a Backspace, a Ctrl+C, a Ctrl+X or a Ctrl+V asks for.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_sample is null)
        {
            return;
        }

        var askedEditKind = (e.Key, e.KeyModifiers) switch
        {
            (_, KeyModifiers.None) when WindowKeys.IsDeleteKey(e.Key) => SampleEditKind.Delete,
            (Key.C, KeyModifiers.Control) => SampleEditKind.Copy,
            (Key.X, KeyModifiers.Control) => SampleEditKind.Cut,
            (Key.V, KeyModifiers.Control) => SampleEditKind.Paste,
            _ => (SampleEditKind?)null,
        };
        if (askedEditKind is not { } asked)
        {
            return;
        }

        e.Handled = true;
        EditRequested?.Invoke(asked);
    }

    // Finishes a fit that was waiting for a width and holds the view inside the new size.
    private void FollowNewSize()
    {
        if (_isFitPending)
        {
            FitWholeSample();
        }

        ClampAllAndReportView();
    }

    // Puts the view where a freshly opened sample starts: the whole sample in view, the marker at the start and nothing selected.
    private void ResetView()
    {
        _markerPoint = 0;
        _anchorPoint = 0;
        _focusPoint = 0;
        FitWholeSample();
        RedrawAndReportView();
    }

    // Zooms out to the whole sample, or leaves that to the next resize where there is no width yet.
    private void FitWholeSample()
    {
        _leftmostPoint = 0;
        _isFitPending = PointCount > 0 && Bounds.Width < MinDrawnWidth;
        _pixelsPerPoint = WholeSamplePixelsPerPoint;
    }

    // Holds the zoom, the scroll, the marker and both ends of the selection inside the sample.
    private void ClampViewMarkerAndSelection()
    {
        _pixelsPerPoint = Math.Clamp(_pixelsPerPoint, WholeSamplePixelsPerPoint, MaxPixelsPerPoint);
        _markerPoint = Math.Clamp(_markerPoint, 0, PointCount);
        _anchorPoint = Math.Clamp(_anchorPoint, 0, PointCount);
        _focusPoint = Math.Clamp(_focusPoint, 0, PointCount);
        ClampLeftmostPoint();
    }

    // Holds the zoom, the scroll, the marker and the selection inside the sample, redraws, and reports the view.
    private void ClampAllAndReportView()
    {
        ClampViewMarkerAndSelection();
        RedrawAndReportView();
    }

    // Holds the scroll inside the sample, redraws, and reports the view.
    private void ClampScrollAndReportView()
    {
        ClampLeftmostPoint();
        RedrawAndReportView();
    }

    // Holds the scroll where the view stays inside the sample.
    private void ClampLeftmostPoint()
    {
        _leftmostPoint = Math.Clamp(_leftmostPoint, 0, FurthestLeftmostPoint);
    }

    // Redraws and tells the scrollbar beside the view what can be scrolled through now.
    private void RedrawAndReportView()
    {
        Redraw();
        ViewChanged?.Invoke();
    }

    // Tells the marker layer and the window that the marker moved.
    private void ReportPlaybackMarkerMoved()
    {
        ReportMarkersMoved();
        MarkerMoved?.Invoke();
    }

    // Redraws the selection's wash and tells the window that the selection changed.
    private void RedrawAndReportSelection()
    {
        Redraw();
        SelectionChanged?.Invoke();
    }

    // Moves the selection's far end to a point boundary, its anchor staying where it is.
    private void MoveSelectionFocusTo(int point)
    {
        _focusPoint = point;
        RedrawAndReportSelection();
    }

    // The selection's end a resize toward a point boundary holds still: its start where the boundary is right of its middle, its end otherwise.
    private int SelectionEndHeldFrom(int point)
    {
        return point > (SelectionStart + SelectionEnd) / 2.0 ? SelectionStart : SelectionEnd;
    }

    // Draws the selected points as a wash under the wave.
    private void DrawSelection(DrawingContext dc, double width, double height)
    {
        if (!HasSelection)
        {
            return;
        }

        double left = Math.Max(XOfPoint(SelectionStart), 0);
        double right = Math.Min(XOfPoint(SelectionEnd), width);
        if (right <= left)
        {
            return;
        }

        dc.DrawRectangle(_selectionBrush, null, new Rect(left, 0, right - left, height));
    }

    // Draws one line per pixel column, from the highest to the lowest of the column's points and the point before them, so the columns join.
    private void DrawColumnPeaks(DrawingContext dc, short[] points, double width, double middle)
    {
        int columns = (int)width;
        for (int column = 0; column < columns; column++)
        {
            int first = (int)(_leftmostPoint + column / _pixelsPerPoint);
            int last = (int)(_leftmostPoint + (column + 1) / _pixelsPerPoint);
            if (first >= points.Length)
            {
                break;
            }

            if (last <= first)
            {
                last = first + 1;
            }

            if (last > points.Length)
            {
                last = points.Length;
            }

            int firstSpannedPoint = Math.Max(0, first - 1);
            int low = points[firstSpannedPoint];
            int high = points[firstSpannedPoint];
            for (int i = firstSpannedPoint + 1; i < last; i++)
            {
                if (points[i] < low)
                {
                    low = points[i];
                }

                if (points[i] > high)
                {
                    high = points[i];
                }
            }

            double x = DrawnSurface.PixelCenter(column);
            double top = YOfValue(high, middle);
            double bottom = YOfValue(low, middle);
            if (bottom - top < MinPeakLineLength)
            {
                bottom = top + MinPeakLineLength;
            }

            dc.DrawLine(_wavePen, new Point(x, top), new Point(x, bottom));
        }
    }

    // Draws the points in view joined by lines, each at the center of the stretch it owns.
    private void DrawJoinedPoints(DrawingContext dc, short[] points, double middle)
    {
        var (first, last) = PointsInViewOf(points);
        var previous = CenterOfPoint(points, first, middle);
        for (int i = first + 1; i <= last; i++)
        {
            var next = CenterOfPoint(points, i, middle);
            dc.DrawLine(_wavePen, previous, next);
            previous = next;
        }
    }

    // Draws every point in view as a node.
    private void DrawNodes(DrawingContext dc, short[] points, double middle)
    {
        var (first, last) = PointsInViewOf(points);
        for (int i = first; i <= last; i++)
        {
            dc.DrawEllipse(_nodeBrush, null, CenterOfPoint(points, i, middle), NodeRadius, NodeRadius);
        }
    }

    // The first and last points to draw, one past each edge of the view.
    private (int First, int Last) PointsInViewOf(short[] points)
    {
        int first = Math.Max(0, (int)_leftmostPoint - OffscreenPointsEachSide);
        int last = Math.Min(points.Length - 1, (int)(_leftmostPoint + ViewportPoints) + OffscreenPointsEachSide);
        return (first, last);
    }

    // Where a point is drawn: the center of the stretch it owns, at its value's height.
    private Point CenterOfPoint(short[] points, int index, double middle)
    {
        return new Point(XOfPoint(index) + _pixelsPerPoint / 2, YOfValue(points[index], middle));
    }

    // The height a point's value is drawn at, full scale reaching the view's edges.
    private static double YOfValue(int value, double middle)
    {
        return middle - value / DspPoint.FullScale * middle;
    }

    // Where a point index sits: the left edge of the stretch it owns.
    private double XOfPoint(double index)
    {
        return (index - _leftmostPoint) * _pixelsPerPoint;
    }

    // The point boundary nearest a position, the boundary past the last point included.
    private int PointBoundaryNearest(double x)
    {
        return Math.Clamp((int)Math.Round(_leftmostPoint + x / _pixelsPerPoint), 0, PointCount);
    }

    // The block nearest a position, which is always a block the sample has.
    private int BlockNearest(double x)
    {
        return Math.Clamp((int)Math.Round((_leftmostPoint + x / _pixelsPerPoint) / BrrSample.PointsPerBlock), 0, LastBlock);
    }

    // Starts panning the wave sideways with the middle button.
    private void StartPan(PointerPressedEventArgs e)
    {
        _isPanning = true;
        WheelDrag.Follow(this, e, moved => LeftmostPoint = _leftmostPoint - moved.X / _pixelsPerPoint);
    }

    // Starts dragging the loop marker, asking for the block under the press at once.
    private void StartLoopDrag(PointerPressedEventArgs e)
    {
        _isDraggingLoop = true;
        _lastLoopBlockAsked = NoLoopBlockAsked;
        e.Pointer.Capture(this);
        AskForLoopBlockAt(e.GetPosition(this).X);
    }

    // Answers a press here or on the ruler: the left button places and drags the marker, the right sweeps a selection - with Shift, resizes it - and the
    // wheel button places and drags the marker with Shift, drags the loop marker with Ctrl and pans with neither.
    private void TakePress(PointerPressedEventArgs e)
    {
        if (_sample is null)
        {
            return;
        }

        var properties = e.GetCurrentPoint(this).Properties;
        if (WheelDrag.IsPlayheadPress(e))
        {
            StartMarkerDrag(e);
            return;
        }

        if (properties.IsMiddleButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            StartLoopDrag(e);
            return;
        }

        if (properties.IsMiddleButtonPressed)
        {
            StartPan(e);
            return;
        }

        if (properties.IsRightButtonPressed)
        {
            StartSelection(e);
            return;
        }

        if (properties.IsLeftButtonPressed)
        {
            StartMarkerDrag(e);
        }
    }

    // Puts the marker where a press landed and goes on moving it for as long as the button is held.
    private void StartMarkerDrag(PointerPressedEventArgs e)
    {
        Focus();
        _isDraggingMarker = true;
        e.Pointer.Capture(this);
        MoveMarkerTo(PointBoundaryNearest(e.GetPosition(this).X));
    }

    // Moves the marker to the point boundary nearest a position, where that moves it.
    private void FollowMarkerDrag(double x)
    {
        int point = PointBoundaryNearest(x);
        if (point != _markerPoint)
        {
            MoveMarkerTo(point);
        }
    }

    // Starts a selection where a press landed, or with Shift resizes the selection to it, the marker staying where it is.
    private void StartSelection(PointerPressedEventArgs e)
    {
        Focus();
        _isSelecting = true;
        e.Pointer.Capture(this);
        int point = PointBoundaryNearest(e.GetPosition(this).X);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ResizeSelectionTo(point);
            return;
        }

        Select(point, point);
    }

    // Moves the selection's far end to the point boundary nearest a position, where that moves it.
    private void FollowSelection(double x)
    {
        int point = PointBoundaryNearest(x);
        if (point != _focusPoint)
        {
            MoveSelectionFocusTo(point);
        }
    }

    // Carries whichever drag is held to a position, and pans the view toward it while it is past either edge.
    private void FollowDragAt(double x)
    {
        if (_isDraggingLoop)
        {
            AskForLoopBlockAt(x);
        }

        if (_isDraggingMarker)
        {
            FollowMarkerDrag(x);
        }

        if (_isSelecting)
        {
            FollowSelection(x);
        }

        _edgeScroll.FollowDrag(Offset.X + x, null, FollowCursor);
    }

    // Re-runs the drag held from where the cursor now is over the view, answering whether one is held.
    private bool FollowCursor()
    {
        return _windowCursor.FollowOver(this, IsDragHeld, cursor => FollowDragAt(cursor.X));
    }

    // Zooms by the wheel's notches, keeping the point under the cursor where it is.
    private void ZoomAroundCursor(PointerWheelEventArgs e)
    {
        double x = e.GetPosition(this).X;
        double pixelsPerPointBefore = _pixelsPerPoint;
        double viewStartBefore = _leftmostPoint * pixelsPerPointBefore;
        PixelsPerPoint = _pixelsPerPoint * RollMetrics.ZoomFactorForNotches(e.Delta.Y);
        LeftmostPoint = RollMetrics.ViewStartHolding(viewStartBefore, x, pixelsPerPointBefore, _pixelsPerPoint) / _pixelsPerPoint;
    }

    // Asks for the block nearest a position as the loop's start, where it is not the block last asked for.
    private void AskForLoopBlockAt(double x)
    {
        if (_sample is null || PointCount == 0 || Bounds.Width < MinDrawnWidth)
        {
            return;
        }

        int block = BlockNearest(x);
        if (block == _lastLoopBlockAsked)
        {
            return;
        }

        _lastLoopBlockAsked = block;
        LoopBlockDragged?.Invoke(block);
    }
}
