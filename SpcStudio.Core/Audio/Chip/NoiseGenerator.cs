using SpcStudio.Model;

namespace SpcStudio.Audio;

// The chip's one noise generator: a free-running shift register stepped at the song's noise clock, sampled into a buffer every voice playing noise reads.
internal sealed class NoiseGenerator
{
    private int _lfsrState = SnesNoise.InitialLfsrState;
    private int _samplesUntilStep = 1;
    private int _clock;

    // The samples between two steps of the shift register at the clock, none where it is stopped.
    private int Period
    {
        get
        {
            return SnesDspTables.RatePeriods[_clock];
        }
    }

    // Runs the generator over a stretch of a buffer, one point per output sample.
    public void Fill(short[] points, int start, int count)
    {
        int period = Period;

        for (int i = 0; i < count; i++)
        {
            if (period > 0 && --_samplesUntilStep <= 0)
            {
                _lfsrState = SnesNoise.NextLfsrState(_lfsrState);
                _samplesUntilStep = period;
            }

            points[start + i] = SnesNoise.SampleForLfsrState(_lfsrState);
        }
    }

    // Sets the song's noise clock, the wait to the next step carried over but held within the new period.
    public void SetClock(int clock)
    {
        _clock = SnesNoise.ClockRange.Clamp(clock);

        int period = Period;
        if (period > 0 && _samplesUntilStep > period)
        {
            _samplesUntilStep = period;
        }
    }

    // Clears the song's noise clock.
    public void ClearClock()
    {
        _clock = SnesNoise.MinClock;
    }
}
