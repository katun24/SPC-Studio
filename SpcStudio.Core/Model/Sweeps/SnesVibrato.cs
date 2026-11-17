using System.Collections.Immutable;

namespace SpcStudio.Model;

// The driver's vibrato: what p accepts, the one fade $EA can give it, and the triangle it sweeps the note value with.
public static class SnesVibrato
{
    public const int MinExtent = 0;
    public const int MaxExtent = 255;
    public const int MinSweptExtent = MinExtent + 1;
    public const int MinFadeTicks = 0;
    public const int MaxFadeTicks = 255;
    public const int DefaultExtent = 0;
    public const int CoarseExtent = 0xF1;
    public const int PeakPhase = 0x40;
    public const int StartPhase = 0;
    private const int SignBit = 0x80;
    private const int FallingQuarterBit = 0x40;
    private const int TriangleShift = 2;
    private const int CoarseMultiplierMask = 0x0F;

    public static readonly VibratoSetting[] Settings = Enum.GetValues<VibratoSetting>();
    public static readonly ValueRange ExtentRange = new(MinExtent, MaxExtent);
    public static readonly ValueRange SweptExtentRange = new(MinSweptExtent, MaxExtent);
    public static readonly ValueRange FadeTicksRange = new(MinFadeTicks, MaxFadeTicks);

    // The four values a range holds the field to, the length's being the note's own.
    public static readonly VibratoSetting[] BoundedSettings = [VibratoSetting.Delay, VibratoSetting.Rate, VibratoSetting.Extent, VibratoSetting.Fade];

    // The range one of the five accepts.
    public static ValueRange Range(VibratoSetting setting)
    {
        return setting switch
        {
            VibratoSetting.Delay => NoteSweep.DelayTicksRange,
            VibratoSetting.Length => NoteSweep.LengthTicksRange,
            VibratoSetting.Rate => SnesSweep.RateRange,
            VibratoSetting.Extent => SweptExtentRange,
            _ => FadeTicksRange,
        };
    }

    // What a new song's channel starts each of the three driver values at.
    public static int Default(VibratoSetting setting)
    {
        return setting switch
        {
            VibratoSetting.Delay => SnesSweep.DefaultDelay,
            VibratoSetting.Rate => SnesSweep.DefaultRate,
            _ => DefaultExtent,
        };
    }

    // How much wider the sweep gets on each tick of a fade: the extent over the fade's length, in whole numbers.
    public static int FadeStep(int extent, int fadeTicks)
    {
        return fadeTicks == MinFadeTicks ? 0 : extent / fadeTicks;
    }

    // The longest fade the sweep at an index may hold: none where another sweep on the note already fades, and its own extent otherwise.
    public static int MaxFadeIn(ImmutableArray<NoteVibrato> sweeps, int sweepIndex)
    {
        if (sweeps.IsDefaultOrEmpty || sweepIndex >= sweeps.Length)
        {
            return MinFadeTicks;
        }

        return IsFadeTaken(sweeps, sweepIndex) ? MinFadeTicks : Math.Min(MaxFadeTicks, sweeps[sweepIndex].Extent);
    }

    // Whether a sweep other than the one at an index already fades, which leaves that one no fade to hold.
    public static bool IsFadeTaken(ImmutableArray<NoteVibrato> sweeps, int sweepIndex)
    {
        if (sweeps.IsDefaultOrEmpty)
        {
            return false;
        }

        for (int index = 0; index < sweeps.Length; index++)
        {
            if (index != sweepIndex && sweeps[index].FadeTicks != MinFadeTicks)
            {
                return true;
            }
        }

        return false;
    }

    // The shallowest the sweep at an index may be: no shallower than its own fade is long.
    public static int MinExtentIn(ImmutableArray<NoteVibrato> sweeps, int sweepIndex)
    {
        if (sweeps.IsDefaultOrEmpty || sweepIndex >= sweeps.Length)
        {
            return MinSweptExtent;
        }

        return Math.Max(MinSweptExtent, sweeps[sweepIndex].FadeTicks);
    }

    // The extent after some ticks of a fade have run: a whole number of steps up from nothing, and the exact extent once the fade's length is reached.
    public static int FadedExtent(int extent, int fadeTicks, int steps)
    {
        return steps >= fadeTicks ? extent : (FadeStep(extent, fadeTicks) * steps) & byte.MaxValue;
    }

    // A chain with every value held to its range, at most one sweep fading, and no fade longer than the extent it widens into.
    public static ImmutableArray<NoteVibrato> Sanitized(ImmutableArray<NoteVibrato> sweeps)
    {
        if (sweeps.IsDefaultOrEmpty)
        {
            return [];
        }

        var sanitizedSweeps = ImmutableArray.CreateBuilder<NoteVibrato>(sweeps.Length);
        foreach (var sweep in sweeps)
        {
            var clampedSweep = NoteSweep.WithSpanClamped(sweep, SnesSweep.MaxDelay) with
            {
                Rate = ClampToRange(sweep.Rate, VibratoSetting.Rate),
                Extent = ClampToRange(sweep.Extent, VibratoSetting.Extent),
                FadeTicks = ClampToRange(sweep.FadeTicks, VibratoSetting.Fade),
            };

            // The fade this sweep keeps: none where one in front of it already fades, and never longer than its extent.
            int keptFadeTicks = IsFadeTaken(sanitizedSweeps.ToImmutable(), sanitizedSweeps.Count)
                ? MinFadeTicks
                : Math.Min(clampedSweep.FadeTicks, clampedSweep.Extent);
            sanitizedSweeps.Add(clampedSweep with { FadeTicks = keptFadeTicks });
        }

        return sanitizedSweeps.ToImmutable();
    }

    // The phase a sweep reaches some steps on from a phase, wrapping round at the end of each cycle.
    public static int PhaseAfter(int phase, int rate, int steps)
    {
        return (phase + rate * steps) & byte.MaxValue;
    }

    // The magnitude of the triangle the driver makes of an 8-bit phase, from zero up to the byte's top, its sign being whether the phase is rising.
    public static int TriangleAt(int phase)
    {
        int shiftedPhase = (phase << TriangleShift) & byte.MaxValue;
        return (phase & FallingQuarterBit) != 0 ? shiftedPhase ^ byte.MaxValue : shiftedPhase;
    }

    // Whether the sweep is above the note at a phase, which is the first half of each cycle.
    public static bool IsRisingAt(int phase)
    {
        return (phase & SignBit) == 0;
    }

    // The signed offset a phase and an extent add to the note value, in fine-tune steps.
    public static int OffsetAt(int phase, int extent)
    {
        int triangle = TriangleAt(phase);
        int magnitude = extent < CoarseExtent ? (extent * triangle) >> ByteUnits.BitsPerByte : (extent & CoarseMultiplierMask) * triangle;
        return IsRisingAt(phase) ? magnitude : -magnitude;
    }

    // How far above the note the sweep reaches at its peak, in fine-tune steps.
    public static int PeakOffset(int extent)
    {
        return OffsetAt(PeakPhase, extent);
    }

    // How wide the sweep is either side of the note, in cents.
    public static double PeakCents(int extent)
    {
        return PeakOffset(extent) * (double)SnesPitch.CentsPerSemitone / SnesPitch.FineTuneStepsPerSemitone;
    }

    // The extent whose sweep reaches nearest a width either side of the note, in semitones, the shallowest of two that reach it equally.
    public static int ExtentReaching(double peakSemitones)
    {
        return SnesSweep.NearestSetting(SweptExtentRange, extent => PeakOffset(extent), peakSemitones * SnesPitch.FineTuneStepsPerSemitone);
    }

    // A value held to the range of one of the five.
    private static int ClampToRange(int value, VibratoSetting setting)
    {
        return Range(setting).Clamp(value);
    }
}
