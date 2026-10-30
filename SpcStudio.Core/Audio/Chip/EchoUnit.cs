using SpcStudio.Model;

namespace SpcStudio.Audio;

// The DSP's echo at its native rate: a delay line the enabled voices are written into, an eight-tap FIR over what comes back out of it, feedback into the line and a signed volume into the main mix.
public sealed class EchoUnit
{
    private readonly int[] _historyLeft = new int[SnesEcho.TapCount];
    private readonly int[] _historyRight = new int[SnesEcho.TapCount];
    private int _usedBufferLength = SnesEcho.BufferSamplesFor(SnesEcho.MinDelay);
    private int _largestDelay = SnesEcho.MinDelay;
    private int _bufferPosition;
    private EchoState _state = EchoState.Off;
    private sbyte[] _firCoefficients = SnesEcho.UnpackCoefficients(SnesEcho.PackedPresets[SnesEcho.DefaultFilter]);

    // The delay line on each side, allocated at the shortest delay until a longer one is set.
    private int[] _bufferLeft = new int[SnesEcho.BufferSamplesFor(SnesEcho.MinDelay)];
    private int[] _bufferRight = new int[SnesEcho.BufferSamplesFor(SnesEcho.MinDelay)];

    public EchoState State
    {
        get
        {
            return _state;
        }
    }

    // Takes a new echo state, allocating and clearing a longer buffer where the delay rises past anything the run has used.
    public void SetState(EchoState state)
    {
        _state = state;
        _firCoefficients = SnesEcho.UnpackCoefficients(state.PackedCoefficients);

        if (state.Delay > _largestDelay)
        {
            _largestDelay = state.Delay;
            _bufferLeft = new int[SnesEcho.BufferSamplesFor(_largestDelay)];
            _bufferRight = new int[SnesEcho.BufferSamplesFor(_largestDelay)];
        }

        _usedBufferLength = SnesEcho.BufferSamplesFor(state.Delay);
        _bufferPosition %= _usedBufferLength;
    }

    // Takes the two echo volumes alone, which is what a step of an $F2 fade writes.
    public void SetVolumes(int left, int right)
    {
        _state = _state with { VolumeLeft = left, VolumeRight = right };
    }

    // Empties the line and forgets what has been allocated, for a run that is starting rather than carrying on.
    public void Reset()
    {
        Array.Clear(_historyLeft);
        Array.Clear(_historyRight);
        Array.Clear(_bufferLeft);
        Array.Clear(_bufferRight);
        _bufferPosition = 0;
        _largestDelay = SnesEcho.MinDelay;
        _usedBufferLength = SnesEcho.BufferSamplesFor(SnesEcho.MinDelay);
        _state = EchoState.Off;
        _firCoefficients = SnesEcho.UnpackCoefficients(EchoState.Off.PackedCoefficients);
    }

    // Runs the line over a stretch of the send buses and adds what comes out into the main ones, doing nothing while the echo is off.
    public void Mix(float[] left, float[] right, int[] sendLeft, int[] sendRight, int start, int count)
    {
        if (!_state.IsEnabled)
        {
            return;
        }

        int feedback = SnesEcho.SignedValueOf(_state.Feedback);
        int volumeLeft = SnesEcho.SignedValueOf(_state.VolumeLeft);
        int volumeRight = SnesEcho.SignedValueOf(_state.VolumeRight);

        for (int i = start; i < start + count; i++)
        {
            PushIntoHistory(_historyLeft, _bufferLeft[_bufferPosition]);
            PushIntoHistory(_historyRight, _bufferRight[_bufferPosition]);

            int filteredLeft = FilteredPoint(_historyLeft);
            int filteredRight = FilteredPoint(_historyRight);

            _bufferLeft[_bufferPosition] = Written(sendLeft[i], filteredLeft, feedback);
            _bufferRight[_bufferPosition] = Written(sendRight[i], filteredRight, feedback);

            if (++_bufferPosition >= _usedBufferLength)
            {
                _bufferPosition = 0;
            }

            left[i] += DspPoint.ToLevel((filteredLeft * volumeLeft) >> DspPoint.RegisterProductShift);
            right[i] += DspPoint.ToLevel((filteredRight * volumeRight) >> DspPoint.RegisterProductShift);
        }
    }

    // Moves the FIR's history along by one, the newest point in the last tap.
    private static void PushIntoHistory(int[] history, int point)
    {
        for (int tap = 0; tap < history.Length - 1; tap++)
        {
            history[tap] = history[tap + 1];
        }

        history[^1] = point;
    }

    // What goes back into the line: what the voices sent in, plus what the line feeds back to itself.
    private static int Written(int sent, int filtered, int feedback)
    {
        return DspPoint.Clipped(DspPoint.Clipped(sent) + DspPoint.Clipped((filtered * feedback) >> DspPoint.RegisterProductShift));
    }

    // The eight taps summed under the filter's coefficients.
    private int FilteredPoint(int[] history)
    {
        int sum = 0;
        for (int tap = 0; tap < history.Length; tap++)
        {
            sum += history[tap] * _firCoefficients[tap];
        }

        return DspPoint.Clipped(sum >> DspPoint.RegisterProductShift);
    }
}
