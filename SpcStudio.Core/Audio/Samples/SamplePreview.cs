namespace SpcStudio.Audio;

// The sample editor's own playback: a stretch of one sample's points, stepped through at a pitch register and read through the DSP's gaussian
// interpolation as a voice reads them, summed straight into the mixer's main buses, wrapping to a loop point or stopping at the end.
public sealed class SamplePreview
{
    public const int NoLoop = -1;
    private readonly object _lock = new();
    private readonly GaussianWindow _interpolation = new();
    private readonly Func<int?> _readNextPoint;
    private short[]? _points;
    private short[]? _pointsAtNextWrap;
    private int _position;
    private int _start;
    private int _end;
    private int _loopPoint = NoLoop;
    private int _pitchRegister;
    private bool _isPlaying;

    // Builds the preview, handing its interpolation the one reader of points it takes them through.
    public SamplePreview()
    {
        _readNextPoint = ReadNextPoint;
    }

    // Whether the preview is sounding.
    public bool IsPlaying
    {
        get
        {
            lock (_lock)
            {
                return _isPlaying;
            }
        }
    }

    // The point the preview has reached, or null where none is loaded.
    public int? Point
    {
        get
        {
            lock (_lock)
            {
                return _points is null ? null : _position;
            }
        }
    }

    // Whether the range holds any point to play.
    private bool HasRangeToPlay
    {
        get
        {
            return _start < _end;
        }
    }

    // Starts previewing a stretch of points at a pitch register, wrapping back to a loop point at the end or stopping there where the loop is negative.
    public void Start(short[] points, int start, int end, int loopPoint, int pitchRegister)
    {
        lock (_lock)
        {
            _points = points;
            SetRange(points, start, end, loopPoint, pitchRegister);
            StartFromRangeStart();
        }
    }

    // Starts previewing a sample whole from its first point at the pitch register the Sample Editor plays it at, wrapping back to its loop point where it loops.
    public void StartWhole(Sample sample)
    {
        Start(sample.Decoded.Points, start: 0, end: sample.Decoded.Points.Length, LoopPointOfWhole(sample), sample.PreviewPitchRegister);
    }

    // Where a sample played whole goes back to at its end: its loop point, or nowhere for a sample that does not loop.
    public static int LoopPointOfWhole(Sample sample)
    {
        return sample.Decoded.DoesLoop ? sample.Decoded.LoopStartPoint : NoLoop;
    }

    // Re-aims a loaded preview at new points, a new range and a new pitch register, the points taken up where the position already is.
    public void Retarget(short[] points, int start, int end, int loopPoint, int pitchRegister)
    {
        RetargetLoaded(points, start, end, loopPoint, pitchRegister, TakePoints);
    }

    // Re-aims a loaded preview at new points, a new range and a new pitch register, the points waiting for the preview's own loop wrap where it is
    // looping and playing.
    public void RetargetAtLoop(short[] points, int start, int end, int loopPoint, int pitchRegister)
    {
        RetargetLoaded(points, start, end, loopPoint, pitchRegister, TakePointsAtLoopWrap);
    }

    // Puts a loaded preview's position back at the start of its range.
    public void SeekToStart()
    {
        lock (_lock)
        {
            if (_points is not null)
            {
                _position = _start;
            }
        }
    }

    // Plays on from where the preview was paused, or from the start again where it had reached the end.
    public void Resume()
    {
        lock (_lock)
        {
            if (_points is null)
            {
                return;
            }

            if (_position >= _end)
            {
                StartFromRangeStart();
                return;
            }

            _isPlaying = HasRangeToPlay;
        }
    }

    // Stops the preview and leaves the position where it is.
    public void Pause()
    {
        lock (_lock)
        {
            _isPlaying = false;
        }
    }

    // Stops the preview and unloads it, so there is no position to draw.
    public void Stop()
    {
        lock (_lock)
        {
            _pointsAtNextWrap = null;
            _isPlaying = false;
            _points = null;
        }
    }

    // Sums the preview's next points into a stretch of the main buses, centered.
    internal void MixInto(float[] left, float[] right, int start, int count)
    {
        lock (_lock)
        {
            if (!_isPlaying || _points is null)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                float nextPointLevel = DspPoint.ToLevel(_interpolation.InterpolatedPoint);
                left[start + i] += nextPointLevel;
                right[start + i] += nextPointLevel;
                if (!_interpolation.TryAdvance(_pitchRegister, _readNextPoint))
                {
                    _isPlaying = false;
                    return;
                }
            }
        }
    }

    // Puts the position at the start of the range with an empty interpolation window, as a key-on does, and plays where there is anything to play.
    private void StartFromRangeStart()
    {
        _position = _start;
        _interpolation.Clear();
        _isPlaying = HasRangeToPlay;
    }

    // The next point the interpolation takes in, wrapping to the loop point and taking up points waiting for the wrap there, or null at the end of a
    // preview that does not loop.
    private int? ReadNextPoint()
    {
        if (_position >= _end)
        {
            if (_loopPoint == NoLoop)
            {
                return null;
            }

            _position = _loopPoint;
            TakePointsWaitingForWrap();
        }

        return _points![_position++];
    }

    // Re-aims a loaded preview at new points, a new range and a new pitch register, the points handed over as the re-aim asks, and leaves an unloaded
    // one alone.
    private void RetargetLoaded(short[] points, int start, int end, int loopPoint, int pitchRegister, Action<short[]> takePoints)
    {
        lock (_lock)
        {
            if (_points is null)
            {
                return;
            }

            SetRange(points, start, end, loopPoint, pitchRegister);
            takePoints(points);
            KeepPositionInRange();
        }
    }

    // Reads from new points at once, dropping any that were waiting for the wrap.
    private void TakePoints(short[] points)
    {
        _points = points;
        _pointsAtNextWrap = null;
    }

    // Stages new points for the loop wrap where the preview is looping and playing, and takes them at once otherwise.
    private void TakePointsAtLoopWrap(short[] points)
    {
        if (_loopPoint != NoLoop && _isPlaying)
        {
            _pointsAtNextWrap = points;
            return;
        }

        TakePoints(points);
    }

    // Holds a range and a loop point to the points they are over, a loop at or past the end being no loop, and takes the pitch register to step at.
    private void SetRange(short[] points, int start, int end, int loopPoint, int pitchRegister)
    {
        _end = Math.Clamp(end, 0, points.Length);
        _start = Math.Clamp(start, 0, _end);
        _loopPoint = loopPoint >= 0 && loopPoint < _end ? loopPoint : NoLoop;
        _pitchRegister = pitchRegister;
    }

    // Moves a position left outside the range back to its start.
    private void KeepPositionInRange()
    {
        if (_position < _start || _position >= _end)
        {
            _position = _start;
        }
    }

    // Takes up any points waiting for the wrap, the range and the position held to them.
    private void TakePointsWaitingForWrap()
    {
        if (_pointsAtNextWrap is not { } waiting)
        {
            return;
        }

        TakePoints(waiting);
        _end = Math.Min(_end, waiting.Length);
        if (_position >= waiting.Length)
        {
            _position = 0;
        }
    }
}
