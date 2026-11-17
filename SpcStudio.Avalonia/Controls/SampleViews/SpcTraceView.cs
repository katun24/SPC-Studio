using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using SpcStudio.Audio;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The SPC Player's canvas: the whole song across, every voice of the chip down or one voice alone, and a block per key-on placed by its pitch, with a
// seconds ruler, and where the loop point and the playhead stand.
public sealed class SpcTraceView : MarkedSurface
{
    public const double UnityPitchOctave = 12;
    private const double MinRowHeight = 26;
    private const double BlockHeight = 5;
    private const double MinBlockWidth = 2;
    private const double DefaultLowOctave = 8;
    private const double DefaultHighOctave = 13;
    private const double MinSpanOctaves = 1;
    private const double RangePaddingOctaves = 1.0 / 12;
    private const double MinVisibleSeconds = 1;
    private const double FallbackPixelsPerSecond = 1;
    private const double InitialPixelsPerSecond = 40;
    private const double LowestDrawnPitch = 1;

    public static readonly IBrush RowBrush = new SolidColorBrush(Palette.SpcTraceRow);
    private static readonly double[] LabelStepsSeconds = [0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60];
    private static readonly RulerScale SecondsScale = new(LabelStepsSeconds, Space.Px54, SecondsLabel);
    private readonly IBrush _backgroundBrush = new SolidColorBrush(Palette.SpcTraceBackground);
    private readonly IBrush _alternateRowBrush = new SolidColorBrush(Palette.SpcTraceAlternateRow);
    private readonly IPen _rowLinePen = new Pen(new SolidColorBrush(Palette.CharcoalGray), 1);
    private readonly IPen _octaveLinePen = new Pen(new SolidColorBrush(Palette.SpcTraceOctaveLine), 1);
    private readonly IBrush _grayedSampleBrush = new SolidColorBrush(Palette.AshGray);
    private readonly Dictionary<int, int> _colorIndexOfSrcn = [];
    private readonly Dictionary<int, IBrush> _fillOfSrcn = [];
    private readonly (double Low, double High)[] _rowLog2PitchRanges = new (double, double)[SnesDspRegister.VoiceCount];
    private readonly double[] _stackedRowHeightShares = new double[SnesDspRegister.VoiceCount];
    private readonly WindowCursor _windowCursor = new();
    private SpcTrace? _trace;
    private bool _isDraggingPlayhead;
    private long _dragStartFrame;
    private bool _isPanning;
    private double _pixelsPerSecond = InitialPixelsPerSecond;
    private int? _voiceShownAlone;
    private int? _sampleShownInColor;
    private IReadOnlySet<int> _silencedSrcns = new HashSet<int>();
    private long _position;

    // Builds the canvas, which clips what it draws and never takes the focus, with every voice's row as tall as the others.
    public SpcTraceView()
    {
        ClipToBounds = true;
        Focusable = false;
        ShareRowsEvenly();
    }

    // Raised whenever where a pitch is drawn may have moved - a new trace, the other view, a new height.
    public event Action? RowsChanged;

    // Raised with the frame a click landed on, which is where playback is put.
    public event Action<long>? PositionRequested;

    // Raised with the voice a click landed on a block of, after the position it landed on.
    public event Action<int>? VoicePicked;

    // Raised with the frame a press on the seconds ruler landed on, after the position it landed on.
    public event Action<long>? RulerPressed;

    // Raised when a press on the canvas ends, by its release or by the pointer being taken away.
    public event Action? PressEnded;

    // Raised with the horizontal offset the scroll viewer around the canvas is asked to take.
    public event Action<double>? ScrollRequested;

    // Raised with where across the canvas a dragged playhead has been taken to.
    public event Action<double>? DragAt;

    // How wide the whole trace is at the current zoom.
    public double ContentWidth
    {
        get
        {
            return _trace is null ? 0 : Math.Max(1, _trace.LengthSeconds * _pixelsPerSecond);
        }
    }

    // The one voice drawn across the whole height, or null for every voice stacked.
    public int? VoiceShownAlone
    {
        get
        {
            return _voiceShownAlone;
        }
        set
        {
            if (_voiceShownAlone == value)
            {
                return;
            }

            _voiceShownAlone = value;
            RedrawMovedRows();
        }
    }

    // The sample whose blocks keep their color while every other block is drawn gray, or null for every block in its own sample's color.
    public int? SampleShownInColor
    {
        get
        {
            return _sampleShownInColor;
        }
        set
        {
            if (_sampleShownInColor == value)
            {
                return;
            }

            _sampleShownInColor = value;
            Redraw();
        }
    }

    // The samples whose blocks are drawn gray while no sample is shown in color, being silenced by the Samples table's mutes and solos.
    public IReadOnlySet<int> SilencedSrcns
    {
        get
        {
            return _silencedSrcns;
        }
        set
        {
            _silencedSrcns = value;
            Redraw();
        }
    }

    // Whether the playhead is being dragged.
    public bool IsDraggingPlayhead
    {
        get
        {
            return _isDraggingPlayhead;
        }
    }

    // Whether the canvas is being panned with the middle button.
    public bool IsPanning
    {
        get
        {
            return _isPanning;
        }
    }

    // Where the playhead is drawn, in frames of the chip's rate.
    public long Position
    {
        get
        {
            return _position;
        }
        set
        {
            _position = value;
            ReportMarkersMoved();
        }
    }

    // Where across the canvas the playhead is drawn, or null where there is no trace.
    public override double? PlayheadX
    {
        get
        {
            return _trace is null ? null : XOfFrame(FoldedIntoLoop(Position));
        }
    }

    // Where across the canvas the loop point is drawn, or null where the trace found none.
    public override double? LoopMarkerX
    {
        get
        {
            return _trace?.LoopFrame is long loopFrame ? XOfFrame(loopFrame) : null;
        }
    }

    // Whether the loop point is drawn on, which it is wherever there is one.
    public override bool IsLoopMarkerOn
    {
        get
        {
            return LoopMarkerX is not null;
        }
    }

    // How far down the canvas the markers' lines start, which is the seconds ruler's foot.
    public override double MarkerLineTop
    {
        get
        {
            return RulerBand.Height;
        }
    }

    // How tall the rows are together: the height on offer, never less than the shortest row for each row drawn.
    private double RowsHeight
    {
        get
        {
            int rowCount = ShownVoices.Count;
            return rowCount * Math.Max(MinRowHeight, (Bounds.Height - RulerBand.Height) / rowCount);
        }
    }

    // The voices a row is drawn for, top to bottom: every voice while stacked, or the one shown alone.
    private IReadOnlyList<int> ShownVoices
    {
        get
        {
            return _voiceShownAlone is int voice ? [voice] : [.. Enumerable.Range(0, SnesDspRegister.VoiceCount)];
        }
    }

    // The scroll viewer the canvas stands in, if any.
    private ScrollViewer? ParentScrollViewer
    {
        get
        {
            return this.FindAncestorOfType<ScrollViewer>();
        }
    }

    // How wide the view onto the canvas is, falling back on the canvas's own width before there is a viewer to ask.
    private double ViewWidth
    {
        get
        {
            double width = ParentScrollViewer?.Viewport.Width ?? 0;
            return width > 0 ? width : Bounds.Width;
        }
    }

    // Where the view onto the canvas starts.
    private double ViewLeft
    {
        get
        {
            return ParentScrollViewer?.Offset.X ?? 0;
        }
    }

    // The zoom's floor: the whole trace across the view.
    private double MinPixelsPerSecond
    {
        get
        {
            if (_trace is null || _trace.LengthSeconds <= 0 || ViewWidth <= 0)
            {
                return FallbackPixelsPerSecond;
            }

            return ViewWidth / _trace.LengthSeconds;
        }
    }

    // The zoom's ceiling: the closest view across the canvas, never below the floor.
    private double MaxPixelsPerSecond
    {
        get
        {
            return Math.Max(MinPixelsPerSecond, ViewWidth <= 0 ? FallbackPixelsPerSecond : ViewWidth / MinVisibleSeconds);
        }
    }

    // Shows a trace, or clears the canvas for null, fitting each row's pitch range and the whole trace into a view's width.
    public void SetTrace(SpcTrace? trace, double viewportWidth)
    {
        _trace = trace;
        Position = 0;
        _colorIndexOfSrcn.Clear();
        _fillOfSrcn.Clear();
        if (trace is not null)
        {
            for (int index = 0; index < trace.HeardSrcns.Count; index++)
            {
                _colorIndexOfSrcn[trace.HeardSrcns[index]] = index;
            }
        }

        FitPitchRanges();
        ShareRowsEvenly();
        FitWholeTrace(viewportWidth);
        RowsChanged?.Invoke();
    }

    // What a voice's row spans, as log2s of PITCH register values.
    public (double Low, double High) PitchRangeOf(int voice)
    {
        return _rowLog2PitchRanges[voice];
    }

    // Where a pitch, as the log2 of a PITCH register value, is drawn in a voice's row, as the y of the middle of a block at that pitch.
    public double YOfPitch(int voice, double octave)
    {
        var (low, high) = _rowLog2PitchRanges[voice];
        double placeInRange = (octave - low) / (high - low);
        return RowTop(voice) + (1 - placeInRange) * (RowHeightOf(voice) - BlockHeight) + BlockHeight / 2;
    }

    // Puts the zoom where the whole trace fits a view's width.
    public void FitWholeTrace(double viewportWidth)
    {
        if (_trace is not null && viewportWidth > 0)
        {
            _pixelsPerSecond = ClampedPixelsPerSecond(viewportWidth / _trace.LengthSeconds);
        }

        RemeasureAndRedraw();
    }

    // Puts the zoom back inside the range the view's current width allows, and answers whether it moved.
    public bool ClampZoom()
    {
        return TrySetPixelsPerSecond(_pixelsPerSecond);
    }

    // Where a playback position is drawn: past the trace's end folded back into its loop, or held at the end where there is none.
    public long FoldedIntoLoop(long position)
    {
        return _trace?.FoldedIntoLoop(position) ?? position;
    }

    // The voices a block of which is sounding at a frame, in order.
    public IEnumerable<int> VoicesSoundingAt(long frame)
    {
        return BlocksSoundingBetween(frame, frame).Select(block => block.Voice).Distinct().Order();
    }

    // The blocks sounding anywhere from one playback position to a later one, both folded into the loop, a stretch running over the loop's end
    // taking in the loop's start up to the later position too.
    public IEnumerable<SpcTraceBlock> BlocksSoundingBetweenPositions(long fromPosition, long toPosition)
    {
        long fromFrame = FoldedIntoLoop(fromPosition);
        long toFrame = FoldedIntoLoop(toPosition);
        if (fromFrame <= toFrame || _trace?.LoopFrame is not long loopFrame)
        {
            return BlocksSoundingBetween(fromFrame, toFrame);
        }

        return BlocksSoundingBetween(fromFrame, _trace.LengthFrames).Concat(BlocksSoundingBetween(loopFrame, toFrame));
    }

    // The blocks sounding anywhere from one frame of the trace to another, both included, or none where there is no trace.
    private IEnumerable<SpcTraceBlock> BlocksSoundingBetween(long fromFrame, long toFrame)
    {
        if (_trace is null)
        {
            return [];
        }

        return _trace.Blocks.Where(block => block.IsSoundingBetween(fromFrame, toFrame));
    }

    // Re-runs the playhead's drag from where the cursor now is over the canvas, and answers whether a drag is running.
    public bool FollowCursor()
    {
        return _windowCursor.FollowOver(this, _isDraggingPlayhead && _trace is not null, cursor => FollowPlayheadDragAt(cursor.X));
    }

    // Draws the rows, the octave lines, the seconds ruler and the blocks.
    public override void Render(DrawingContext dc)
    {
        double width = Math.Max(ContentWidth, Bounds.Width);
        double height = RulerBand.Height + RowsHeight;
        dc.DrawRectangle(_backgroundBrush, null, new Rect(0, 0, width, height));
        DrawRows(dc, width);
        DrawOctaveLines(dc, width);
        double left = ViewLeft;
        double right = left + (Bounds.Width > 0 ? Bounds.Width : width);
        DrawSecondsRuler(dc, width, left, right);
        if (_trace is null)
        {
            return;
        }

        foreach (var block in _trace.Blocks)
        {
            if (_voiceShownAlone is null || block.Voice == _voiceShownAlone)
            {
                DrawBlock(dc, block, left, right);
            }
        }
    }

    // Tells the markers the canvas changed size, and the pitch ruler the rows moved where its height changed.
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ReportMarkersMoved();
        if (e.HeightChanged)
        {
            RowsChanged?.Invoke();
        }
    }

    // Asks to be as wide as the whole trace and at least tall enough for every voice's row at its shortest.
    protected override Size MeasureOverride(Size availableSize)
    {
        double leastHeight = RulerBand.Height + SnesDspRegister.VoiceCount * MinRowHeight;
        return new Size(ContentWidth, double.IsInfinity(availableSize.Height) ? leastHeight : Math.Max(leastHeight, availableSize.Height));
    }

    // Starts a pan on the middle button, or with Shift puts playback where the press landed; on the left one puts playback there and picks the voice
    // of a block it landed on.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _windowCursor.Remember(this, e);
        if (_trace is null)
        {
            return;
        }

        var buttons = e.GetCurrentPoint(this).Properties;
        if (WheelDrag.IsPlayheadPress(e))
        {
            StartPlayheadDrag(e.GetPosition(this).X);
        }
        else if (buttons.IsMiddleButtonPressed)
        {
            _isPanning = true;
            WheelDrag.Follow(this, e, moved => ScrollRequested?.Invoke(ViewLeft - moved.X));
            e.Handled = true;
            return;
        }
        else if (buttons.IsLeftButtonPressed)
        {
            StartPlayheadPress(e.GetPosition(this));
        }
        else
        {
            return;
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    // Pans the canvas sideways, or moves the dragged playhead without seeking.
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _windowCursor.Remember(this, e);
        if (_isPanning)
        {
            return;
        }

        if (!_isDraggingPlayhead || _trace is null)
        {
            return;
        }

        FollowPlayheadDragAt(e.GetPosition(this).X);
        e.Handled = true;
    }

    // Ends a pan, or ends the press, seeking where a drag that moved ended.
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Middle && _isPanning)
        {
            return;
        }

        if (_isDraggingPlayhead)
        {
            _isDraggingPlayhead = false;
            if (Position != _dragStartFrame)
            {
                PositionRequested?.Invoke(Position);
            }
        }

        PressEnded?.Invoke();
    }

    // Drops a drag or a pan when something else takes the pointer, and ends the press.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDraggingPlayhead = false;
        _isPanning = false;
        PressEnded?.Invoke();
    }

    // Zooms the seconds on Ctrl and the wheel, and the voice's row under the cursor on Alt while the voices are stacked.
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_trace is null)
        {
            return;
        }

        double zoomFactor = RollMetrics.ZoomFactorForNotches(e.Delta.Y);
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            ZoomSecondsUnderCursor(e.GetPosition(this), zoomFactor);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Alt && _voiceShownAlone is null && VoiceAt(e.GetPosition(this).Y) is int voice)
        {
            ZoomRow(voice, zoomFactor);
            e.Handled = true;
        }
    }

    // Puts playback where a press landed and starts dragging the playhead from there.
    private void StartPlayheadDrag(double x)
    {
        _dragStartFrame = FrameAt(x);
        _isDraggingPlayhead = true;
        PositionRequested?.Invoke(_dragStartFrame);
    }

    // Puts playback where a left press landed, and sounds the ruler there or picks the voice of a block it landed on.
    private void StartPlayheadPress(Point pressedAt)
    {
        StartPlayheadDrag(pressedAt.X);
        if (pressedAt.Y < RulerBand.Height)
        {
            RulerPressed?.Invoke(_dragStartFrame);
        }
        else if (BlockAt(pressedAt) is { } block)
        {
            VoicePicked?.Invoke(block.Voice);
        }
    }

    // Moves the dragged playhead to the frame at a position, without seeking, and says where it was taken.
    private void FollowPlayheadDragAt(double x)
    {
        Position = FrameAt(x);
        DragAt?.Invoke(x);
    }

    // Zooms the seconds by a factor, keeping the second under the cursor under it.
    private void ZoomSecondsUnderCursor(Point cursor, double zoomFactor)
    {
        double pixelsPerSecondBefore = _pixelsPerSecond;
        if (TrySetPixelsPerSecond(_pixelsPerSecond * zoomFactor))
        {
            double viewLeft = ViewLeft;
            ScrollRequested?.Invoke(Math.Max(0, RollMetrics.ViewStartHolding(viewLeft, cursor.X - viewLeft, pixelsPerSecondBefore, _pixelsPerSecond)));
        }
    }

    // Zooms one voice's row by a factor, the other voices' rows giving way so the rows still fill the canvas.
    private void ZoomRow(int voice, double zoomFactor)
    {
        double rowsHeight = RowsHeight;
        double[] heights = [.. _stackedRowHeightShares.Select(share => share * rowsHeight)];
        if (!StackedHeights.TryZoomOne(heights, voice, zoomFactor, MinRowHeight, rowsHeight, 0))
        {
            return;
        }

        for (int index = 0; index < heights.Length; index++)
        {
            _stackedRowHeightShares[index] = heights[index] / rowsHeight;
        }

        RedrawMovedRows();
    }

    // Redraws the canvas and tells the pitch ruler after the rows moved.
    private void RedrawMovedRows()
    {
        Redraw();
        RowsChanged?.Invoke();
    }

    // Measures the canvas again at the zoom it now has, and redraws it.
    private void RemeasureAndRedraw()
    {
        InvalidateMeasure();
        Redraw();
    }

    // Puts the zoom at a number of pixels a second, held inside the range the view allows, and answers whether it moved.
    private bool TrySetPixelsPerSecond(double pixelsPerSecond)
    {
        double pixelsPerSecondBefore = _pixelsPerSecond;
        _pixelsPerSecond = ClampedPixelsPerSecond(pixelsPerSecond);
        if (Math.Abs(_pixelsPerSecond - pixelsPerSecondBefore) < double.Epsilon)
        {
            return false;
        }

        RemeasureAndRedraw();
        return true;
    }

    // Gives every voice's row the same part of the rows' height.
    private void ShareRowsEvenly()
    {
        Array.Fill(_stackedRowHeightShares, 1.0 / SnesDspRegister.VoiceCount);
    }

    // A ruler label: minutes and seconds, to the precision the step needs.
    private static string SecondsLabel(double second, double stepSeconds)
    {
        int minutes = (int)(second / TimeUnits.SecondsPerMinute);
        double seconds = second - minutes * TimeUnits.SecondsPerMinute;
        return stepSeconds < 1 ? $"{minutes}:{seconds:00.0}" : $"{minutes}:{seconds:00}";
    }

    // Works out what each voice's row spans: the lowest and highest pitch it plays, padded either side and never narrower than the least span.
    private void FitPitchRanges()
    {
        for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
        {
            _rowLog2PitchRanges[voice] = PitchRangePlayedBy(voice);
        }
    }

    // The span a voice's row is given from the pitches the voice plays, or the default span where it plays none.
    private (double Low, double High) PitchRangePlayedBy(int voice)
    {
        double low = double.MaxValue;
        double high = double.MinValue;
        foreach (var block in _trace?.Blocks ?? [])
        {
            if (block.Voice != voice)
            {
                continue;
            }

            foreach (var note in block.Notes)
            {
                if (note.Pitch <= 0)
                {
                    continue;
                }

                double octave = Math.Log2(note.Pitch);
                low = Math.Min(low, octave);
                high = Math.Max(high, octave);
            }
        }

        if (low > high)
        {
            return (DefaultLowOctave, DefaultHighOctave);
        }

        low -= RangePaddingOctaves;
        high += RangePaddingOctaves;
        double missingOctaves = MinSpanOctaves - (high - low);
        if (missingOctaves > 0)
        {
            low -= missingOctaves / 2;
            high += missingOctaves / 2;
        }

        return (low, high);
    }

    // Where a voice's row starts - under the rows of the voices before it while stacked, and the same place for every voice while one is shown alone.
    private double RowTop(int voice)
    {
        if (_voiceShownAlone is not null)
        {
            return RulerBand.Height;
        }

        return RulerBand.Height + _stackedRowHeightShares.Take(voice).Sum() * RowsHeight;
    }

    // How tall a voice's row is: its share of the rows' height while stacked, or all of it while one voice is shown alone.
    private double RowHeightOf(int voice)
    {
        return _voiceShownAlone is null ? _stackedRowHeightShares[voice] * RowsHeight : RowsHeight;
    }

    // The voice whose row a height falls in, or null on the seconds ruler or below the rows.
    private int? VoiceAt(double y)
    {
        if (y < RulerBand.Height)
        {
            return null;
        }

        foreach (int voice in ShownVoices)
        {
            if (y < RowTop(voice) + RowHeightOf(voice))
            {
                return voice;
            }
        }

        return null;
    }

    // A zoom held inside the range the view allows.
    private double ClampedPixelsPerSecond(double pixelsPerSecond)
    {
        return Math.Clamp(pixelsPerSecond, MinPixelsPerSecond, MaxPixelsPerSecond);
    }

    // The frame under a position, held inside the trace.
    private long FrameAt(double x)
    {
        if (_trace is null)
        {
            return 0;
        }

        return Math.Clamp((long)(x / _pixelsPerSecond * DspPoint.SampleRate), 0, _trace.LengthFrames);
    }

    // Where a frame is drawn.
    private double XOfFrame(long frame)
    {
        return frame / DspPoint.SampleRate * _pixelsPerSecond;
    }

    // The block under a point, or null where the press was on the ruler or on bare row.
    private SpcTraceBlock? BlockAt(Point point)
    {
        if (_trace is null || VoiceAt(point.Y) is not int voice)
        {
            return null;
        }

        foreach (var block in _trace.Blocks)
        {
            double left = XOfFrame(block.Start);
            if (block.Voice == voice && point.X >= left && point.X <= Math.Max(left + MinBlockWidth, XOfFrame(block.End)))
            {
                return block;
            }
        }

        return null;
    }

    // Draws the rows in two alternating fills, each with a line along its top.
    private void DrawRows(DrawingContext dc, double width)
    {
        var voices = ShownVoices;
        for (int row = 0; row < voices.Count; row++)
        {
            int voice = voices[row];
            double top = RowTop(voice);
            dc.DrawRectangle(row % 2 == 0 ? RowBrush : _alternateRowBrush, null, new Rect(0, top, width, RowHeightOf(voice)));
            dc.DrawLine(_rowLinePen, new Point(0, top), new Point(width, top));
        }
    }

    // Draws a line at each octave across the one voice shown alone, and nothing while the voices are stacked.
    private void DrawOctaveLines(DrawingContext dc, double width)
    {
        if (_voiceShownAlone is not int voice)
        {
            return;
        }

        var (low, high) = _rowLog2PitchRanges[voice];
        for (double octave = Math.Ceiling(low); octave <= high; octave++)
        {
            double y = DrawnSurface.PixelCenter(YOfPitch(voice, octave));
            dc.DrawLine(_octaveLinePen, new Point(0, y), new Point(width, y));
        }
    }

    // Draws the seconds band, its labels at the finest step still far enough apart to read, across the stretch the view shows.
    private void DrawSecondsRuler(DrawingContext dc, double width, double left, double right)
    {
        RulerBand.DrawBackground(dc, width);
        if (_trace is null)
        {
            return;
        }

        SecondsScale.Draw(dc, _pixelsPerSecond, 0, left, right, _trace.LengthSeconds, double.NegativeInfinity);
    }

    // Draws one key-on as the notes it sounded, each placed by its pitch in the voice's row.
    private void DrawBlock(DrawingContext dc, SpcTraceBlock block, double viewLeft, double viewRight)
    {
        if (XOfFrame(block.Start) > viewRight || XOfFrame(block.End) + MinBlockWidth < viewLeft)
        {
            return;
        }

        var fill = IsSampleGrayed(block.Srcn) ? _grayedSampleBrush : FillOfSample(block.Srcn);
        var (low, high) = _rowLog2PitchRanges[block.Voice];
        foreach (var note in block.Notes)
        {
            double left = XOfFrame(note.Start);
            double width = Math.Max(XOfFrame(note.End) - left, MinBlockWidth);
            if (left > viewRight || left + width < viewLeft)
            {
                continue;
            }

            double octave = Math.Clamp(Math.Log2(Math.Max(note.Pitch, LowestDrawnPitch)), low, high);
            double top = YOfPitch(block.Voice, octave) - BlockHeight / 2;
            dc.DrawRectangle(fill, null, new Rect(left, top, width, BlockHeight));
        }
    }

    // Whether a sample's blocks are drawn gray: another sample being shown in color, or with none shown, the sample being silenced.
    private bool IsSampleGrayed(int srcn)
    {
        if (_sampleShownInColor is int shownSrcn)
        {
            return srcn != shownSrcn;
        }

        return _silencedSrcns.Contains(srcn);
    }

    // One sample's color, from the palette a new instrument is colored from.
    public Color ColorOfSample(int srcn)
    {
        int colorIndex = _colorIndexOfSrcn.TryGetValue(srcn, out int heardIndex) ? heardIndex : srcn;
        return ChannelPalette.FromPackedRgb(InstrumentPalette.ColorForInstrumentIndex(colorIndex));
    }

    // One sample's color as a brush, made once and held.
    private IBrush FillOfSample(int srcn)
    {
        if (_fillOfSrcn.TryGetValue(srcn, out var brush))
        {
            return brush;
        }

        brush = new SolidColorBrush(ColorOfSample(srcn));
        _fillOfSrcn[srcn] = brush;
        return brush;
    }
}
