namespace SpcStudio.Model;

// The DSP's noise generator: the one LFSR the whole chip shares, and the band of piano-roll rows a noise note's clock is read off.
public static class SnesNoise
{
    public const int MinClock = 0x00;
    public const int MaxClock = 0x1F;
    public const int ClockCount = MaxClock - MinClock + 1;
    public const int MinSemitone = -12;
    public const int MaxSemitone = MinSemitone + ClockCount - 1;
    public const int InitialLfsrState = 0x4000;

    public static readonly ValueRange ClockRange = new(MinClock, MaxClock);
    public static readonly ValueRange SemitoneRange = new(MinSemitone, MaxSemitone);

    // The clock a noise note on a row plays at.
    public static int ClockForSemitone(int semitone)
    {
        return ClockRange.Clamp(semitone - MinSemitone);
    }

    // The row a clock is drawn on, which is what an imported n becomes.
    public static int SemitoneForClock(int clock)
    {
        return ClockRange.Clamp(clock) + MinSemitone;
    }

    // Whether a row is inside the noise band.
    public static bool IsInBand(int semitone)
    {
        return SemitoneRange.Contains(semitone);
    }

    // Brings a row into the noise band, which is what a drag or a re-point past its edge does.
    public static int ClampToBand(int semitone)
    {
        return SemitoneRange.Clamp(semitone);
    }

    // The LFSR's state one step on.
    public static int NextLfsrState(int lfsrState)
    {
        int feedback = (lfsrState << 13) ^ (lfsrState << 14);
        return (feedback & 0x4000) | (lfsrState >> 1);
    }

    // The sample a voice with NON set outputs at an LFSR state: the fifteen-bit value sign-extended and doubled.
    public static short SampleForLfsrState(int lfsrState)
    {
        return (short)(lfsrState * 2);
    }
}
