namespace SpcStudio.Model;

// One of the driver's faded bytes: the 8.8 value, the per-tick delta, the target and the tick countdown - what w, v and y set and $E1, $E8 and $DC ramp.
public readonly record struct SnesFade(int Value16, int Delta16, int Target, int TicksLeft)
{
    public const int NoFade = 0;
    public const int MinFadeTicks = 1;
    public const int MaxFadeTicks = 255;
    public const int FractionBits = 8;
    private const int WordMask = 0xFFFF;

    // The byte the volume routine reads, which is the high half of the 16-bit value.
    public int Value
    {
        get
        {
            return ValueOf(Value16);
        }
    }

    // The fraction below the byte, which is the low half of the 16-bit value.
    public int Fraction
    {
        get
        {
            return FractionOf(Value16);
        }
    }

    // Whether a fade is still running.
    public bool IsFading
    {
        get
        {
            return TicksLeft > 0;
        }
    }

    // Whether a fade is running with nothing to add each tick, which only one armed towards the value it set out from is.
    public bool IsFlat
    {
        get
        {
            return IsFading && Delta16 == 0;
        }
    }

    // Whether a running fade has no addition left to make: a flat one, or one on its last tick, whose one step is the landing.
    public bool HasNoAdditionLeft
    {
        get
        {
            return IsFlat || TicksLeft == 1;
        }
    }

    // Whether a fade of a number of ticks fades at all, which none of no ticks does.
    public static bool DoesFade(int fadeTicks)
    {
        return fadeTicks > NoFade;
    }

    // The byte of any 16-bit value in this layout.
    public static int ValueOf(int value16)
    {
        return (value16 >> FractionBits) & byte.MaxValue;
    }

    // The 16-bit value a byte and the fraction below it make.
    public static int Value16Of(int value, int fraction)
    {
        return value << FractionBits | fraction;
    }

    // The fraction below the byte of any 16-bit value in this layout.
    public static int FractionOf(int value16)
    {
        return value16 & byte.MaxValue;
    }

    // A 16-bit value with nothing fading from it yet, which is what a ramp is set out from.
    public static SnesFade SetOutFrom(int value16)
    {
        return new(value16, 0, ValueOf(value16), NoFade);
    }

    // A byte sitting at a value with nothing fading, which is what a base is.
    public static SnesFade HeldAt(int value)
    {
        return new(value << FractionBits, 0, value, 0);
    }

    // A fade longer than the duration byte can hold, as the driver-legal pieces that play it: even lengths, each ramping to its own interpolated target and the last landing on the fade's own.
    public static IEnumerable<(int Tick, int Ticks, int To)> SplitIntoPieces(int tick, int from, int ticks, int to)
    {
        int pieceCount = Math.Max(1, (ticks + MaxFadeTicks - 1) / MaxFadeTicks);
        int pieceStartTicks = 0;
        for (int piece = 1; piece <= pieceCount; piece++)
        {
            int pieceEndTicks = (int)((long)ticks * piece / pieceCount);
            int pieceTarget = piece == pieceCount ? to : ValueOnLine(from, to, piece, pieceCount);
            yield return (tick + pieceStartTicks, pieceEndTicks - pieceStartTicks, pieceTarget);
            pieceStartTicks = pieceEndTicks;
        }
    }

    // The byte on the straight line from one byte to another a number of steps of a whole along it, rounded the same whichever way the line runs.
    public static int ValueOnLine(int from, int to, int step, int steps)
    {
        return (int)Math.Round(from + (to - from) * (double)step / steps);
    }

    // A plain w, v or y: the byte written and its fraction cleared, a fade in progress left counting.
    public SnesFade WithValueSet(int value)
    {
        return this with { Value16 = (value & byte.MaxValue) << FractionBits };
    }

    // A fading w, v or y: armed to a target over a number of ticks, from the value the byte holds now.
    public SnesFade FadingTo(int target, int ticks)
    {
        return new(Value16, DeltaFor(target - Value, ticks), target & byte.MaxValue, ticks);
    }

    // One tick of the driver's per-tick pass.
    public SnesFade Stepped()
    {
        return Advanced(1);
    }

    // The fade run out: sitting on its target with nothing left to count.
    public SnesFade Landed()
    {
        return Advanced(TicksLeft);
    }

    // How far along its next step the byte has got on a pass of the driver's main loop that carried no tick, for an accumulator holding a fraction of
    // one, signed and zero for a byte that is not fading.
    public int GlideBetweenTicks(int tickFraction)
    {
        if (!IsFading)
        {
            return 0;
        }

        int magnitude = Math.Abs(Delta16) * tickFraction >> FractionBits;
        return Delta16 < 0 ? -magnitude : magnitude;
    }

    // The 16-bit value with the part of its next step a pass between ticks adds, the stored value left where it is.
    public int Value16GlidedBetweenTicks(int tickFraction)
    {
        return (Value16 + GlideBetweenTicks(tickFraction)) & WordMask;
    }

    // The state a number of ticks later, in closed form: a delta added each tick, and the target outright on the tick the countdown reaches zero.
    public SnesFade Advanced(int ticks)
    {
        if (TicksLeft == 0 || ticks <= 0)
        {
            return this;
        }

        if (ticks >= TicksLeft)
        {
            return this with { Value16 = Target << FractionBits, TicksLeft = 0 };
        }

        return this with { Value16 = (Value16 + Delta16 * ticks) & WordMask, TicksLeft = TicksLeft - ticks };
    }

    // The per-tick delta for a difference over a number of ticks, as a signed 8.8 number truncated towards zero.
    private static int DeltaFor(int difference, int ticks)
    {
        int magnitude = (Math.Abs(difference) << FractionBits) / ticks;
        return difference < 0 ? -magnitude : magnitude;
    }
}
