namespace SpcStudio.Audio;

// Linear interpolation from the DSP's rate to the device's: a buffer of stereo samples at the DSP's rate read out at a fractional step, what one read does not
// consume carried over to the front for the next.
internal sealed class StereoResampler(double step)
{
    private const int SamplesPastReadPosition = 2;
    private const int CapacityMargin = 64;
    private float[] _left = [];
    private float[] _right = [];
    private double _fraction;

    public double Step { get; set; } = step;
    public int AvailableSamples { get; set; }

    public float[] Left
    {
        get
        {
            return _left;
        }
    }

    public float[] Right
    {
        get
        {
            return _right;
        }
    }

    // How many samples each buffer holds.
    public int Capacity
    {
        get
        {
            return _left.Length;
        }
    }

    // How many samples the buffers have to hold, counted from the front, for a read of a number of output frames.
    public int SamplesNeededFor(int frames)
    {
        return (int)(_fraction + frames * Step) + SamplesPastReadPosition;
    }

    // Grows the buffers to hold at least a number of samples, answering whether they had to grow.
    public bool EnsureCapacity(int samples)
    {
        if (_left.Length >= samples)
        {
            return false;
        }

        int capacity = samples + CapacityMargin;
        Array.Resize(ref _left, capacity);
        Array.Resize(ref _right, capacity);
        return true;
    }

    // Interpolates interleaved output frames out of the buffers under a gain, held to full scale, and moves what was not consumed to the front.
    public void ReadFrames(float[] buffer, int offset, int frames, float gain)
    {
        double position = _fraction;
        for (int frame = 0; frame < frames; frame++)
        {
            int index = (int)position;
            float fraction = (float)(position - index);
            int at = offset + frame * IAudioSource.ChannelsPerFrame;
            buffer[at] = Interpolated(_left, index, fraction, gain);
            buffer[at + 1] = Interpolated(_right, index, fraction, gain);
            position += Step;
        }

        int consumed = (int)position;
        if (consumed > 0)
        {
            int kept = AvailableSamples - consumed;
            Array.Copy(_left, consumed, _left, 0, kept);
            Array.Copy(_right, consumed, _right, 0, kept);
            AvailableSamples = kept;
        }

        _fraction = position - consumed;
    }

    // Empties the buffers, so the next read starts on the first sample put in.
    public void Reset()
    {
        AvailableSamples = 0;
        _fraction = 0;
    }

    // One channel's value between two samples, under a gain and held to full scale.
    private static float Interpolated(float[] samples, int index, float fraction, float gain)
    {
        float value = (samples[index] + (samples[index + 1] - samples[index]) * fraction) * gain;
        return Math.Clamp(value, -1f, 1f);
    }
}
