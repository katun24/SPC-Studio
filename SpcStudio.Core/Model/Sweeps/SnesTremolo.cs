using System.Collections.Immutable;

namespace SpcStudio.Model;

// The driver's tremolo: what $E5 accepts, and the triangle it ducks the note's own volume with.
public static class SnesTremolo
{
    public const int MinAmount = 0;
    public const int MaxAmount = 255;
    public const int MinDuckedAmount = MinAmount + 1;
    public const int MaxWaveringAmount = MaxAmount - 1;
    public const int DefaultAmount = 0;
    public const int PinnedPhase = 0x80;
    private const int TopHalfBit = 0x80;
    private const int DuckBits = 8;
    private const double DuckDenominator = 1 << DuckBits;

    public static readonly TremoloSetting[] Settings = Enum.GetValues<TremoloSetting>();
    public static readonly ValueRange AmountRange = new(MinAmount, MaxAmount);
    public static readonly ValueRange DuckedAmountRange = new(MinDuckedAmount, MaxAmount);

    // The three values a range holds the field to, the length's being the note's own.
    public static readonly TremoloSetting[] BoundedSettings = [TremoloSetting.Delay, TremoloSetting.Rate, TremoloSetting.Amount];

    // The range one of the four accepts.
    public static ValueRange Range(TremoloSetting setting)
    {
        return setting switch
        {
            TremoloSetting.Delay => NoteSweep.DelayTicksRange,
            TremoloSetting.Length => NoteSweep.LengthTicksRange,
            TremoloSetting.Rate => SnesSweep.RateRange,
            _ => DuckedAmountRange,
        };
    }

    // What a new song's channel starts each of the three driver values at.
    public static int Default(TremoloSetting setting)
    {
        return setting switch
        {
            TremoloSetting.Delay => SnesSweep.DefaultDelay,
            TremoloSetting.Rate => SnesSweep.DefaultRate,
            _ => DefaultAmount,
        };
    }

    // A chain with every value held to its range.
    public static ImmutableArray<NoteTremolo> Sanitized(ImmutableArray<NoteTremolo> tremolos)
    {
        if (tremolos.IsDefaultOrEmpty)
        {
            return [];
        }

        return
        [
            .. tremolos.Select(tremolo => NoteSweep.WithSpanClamped(tremolo, SnesSweep.MaxDelay) with
            {
                Rate = ClampToRange(tremolo.Rate, TremoloSetting.Rate),
                Amount = ClampToRange(tremolo.Amount, TremoloSetting.Amount),
            }),
        ];
    }

    // The triangle the driver makes of an 8-bit phase: from zero up to the byte's top and back, one cycle to the phase's own, unsigned.
    public static int TriangleAt(int phase)
    {
        int doubledPhase = (phase << 1) & byte.MaxValue;
        return (phase & TopHalfBit) != 0 ? doubledPhase ^ byte.MaxValue : doubledPhase;
    }

    // The part of itself the note's volume loses at a phase under an amount, as a fraction of the duck denominator.
    public static int DuckAt(int phase, int amount)
    {
        return (amount * TriangleAt(phase)) >> DuckBits;
    }

    // What the note's volume is left at under a duck.
    public static int DuckedVolume(int noteVolume, int duck)
    {
        return noteVolume - ((duck * noteVolume) >> DuckBits);
    }

    // Where the phase goes on the next tick: on by the rate and wrapped, or pinned at the peak once in the top half under the largest amount.
    public static int NextPhase(int phase, int rate, int amount)
    {
        if ((phase & TopHalfBit) != 0 && amount == MaxAmount)
        {
            return PinnedPhase;
        }

        return (phase + rate) & byte.MaxValue;
    }

    // How far the note is ducked at the wave's trough, as a fraction of its own volume.
    public static double PeakDepth(int amount)
    {
        return DuckAt(PinnedPhase, amount) / DuckDenominator;
    }

    // The amount whose wave ducks nearest a fraction of the note's own volume at its trough, the shallowest of two that duck it equally, never the amount that holds the duck rather than wavering.
    public static int AmountDucking(double depth)
    {
        return SnesSweep.NearestSetting(new ValueRange(MinDuckedAmount, MaxWaveringAmount), PeakDepth, depth);
    }

    // A value held to the range of one of the four.
    private static int ClampToRange(int value, TremoloSetting setting)
    {
        return Range(setting).Clamp(value);
    }
}
