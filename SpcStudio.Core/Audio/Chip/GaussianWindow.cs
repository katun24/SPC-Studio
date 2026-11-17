using SpcStudio.Model;

namespace SpcStudio.Audio;

// The DSP's gaussian interpolation over a sample: the four source points it reads, oldest first, and the position between them that a pitch
// register's step moves on.
internal sealed class GaussianWindow
{
    private const int InterpolationCurveIndexShift = 4;
    private const int GaussianWeightShift = 11;
    private const int OldestPointCurveIndexBase = 255;
    private const int OlderPointCurveIndexBase = 511;
    private const int NewerPointCurveIndexBase = 256;

    private readonly int[] _windowPoints = new int[4];
    private int _position;

    // The point the interpolation reads out of the window at the current position.
    public int InterpolatedPoint
    {
        get
        {
            var weights = SnesDspTables.GaussianWeights;
            int curveEntryIndex = (_position >> InterpolationCurveIndexShift) & byte.MaxValue;
            int interpolatedPoint = (weights[OldestPointCurveIndexBase - curveEntryIndex] * _windowPoints[0]) >> GaussianWeightShift;
            interpolatedPoint += (weights[OlderPointCurveIndexBase - curveEntryIndex] * _windowPoints[1]) >> GaussianWeightShift;
            interpolatedPoint += (weights[NewerPointCurveIndexBase + curveEntryIndex] * _windowPoints[2]) >> GaussianWeightShift;
            interpolatedPoint = (short)interpolatedPoint;
            interpolatedPoint += (weights[curveEntryIndex] * _windowPoints[3]) >> GaussianWeightShift;
            return DspPoint.Clipped(interpolatedPoint);
        }
    }

    // Empties the window and puts the position back at its start, as a key-on does.
    public void Clear()
    {
        Array.Clear(_windowPoints);
        _position = 0;
    }

    // Moves the position on by one pitch step, shifting in a point from the reader for every whole source point crossed, and answers false where the
    // reader has run out.
    public bool TryAdvance(int pitchStep, Func<int?> readNextSourcePoint)
    {
        _position += pitchStep;
        while (_position >= SnesPitch.UnityPitchRegister)
        {
            _position -= SnesPitch.UnityPitchRegister;
            if (readNextSourcePoint() is not int sourcePoint)
            {
                return false;
            }

            _windowPoints[0] = _windowPoints[1];
            _windowPoints[1] = _windowPoints[2];
            _windowPoints[2] = _windowPoints[3];
            _windowPoints[3] = sourcePoint;
        }

        return true;
    }
}
