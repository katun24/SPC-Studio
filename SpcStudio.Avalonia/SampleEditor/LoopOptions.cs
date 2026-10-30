using SpcStudio.Model;

namespace SpcStudio;

// The Sample Editor's Loop Options, which the app remembers between sessions: which loop Find Loop takes, which steps it runs over the loop it found and
// how strongly Smooth Cycles evens out the cycles it is given.
public static class LoopOptions
{
    public const bool DefaultIsShortestLoopPreferred = true;
    public const bool DefaultDoesFindLoopFlattenVolume = false;
    public const bool DefaultDoesFindLoopSmoothCycles = true;
    public const int FullSmoothCyclesEffectPercent = (int)Percent.PerWhole;
    public const int DefaultSmoothCyclesEffectPercent = FullSmoothCyclesEffectPercent;
    private const int LeastSmoothCyclesEffectPercent = 1;

    public static readonly ValueRange SmoothCyclesEffectRange = new(LeastSmoothCyclesEffectPercent, FullSmoothCyclesEffectPercent);
    private static int _smoothCyclesEffectPercent = DefaultSmoothCyclesEffectPercent;

    public static bool IsShortestLoopPreferred { get; set; } = DefaultIsShortestLoopPreferred;
    public static bool DoesFindLoopFlattenVolume { get; set; } = DefaultDoesFindLoopFlattenVolume;
    public static bool DoesFindLoopSmoothCycles { get; set; } = DefaultDoesFindLoopSmoothCycles;

    // How strongly Smooth Cycles evens out the cycles, in percent of the whole way to their average, held inside its range.
    public static int SmoothCyclesEffectPercent
    {
        get
        {
            return _smoothCyclesEffectPercent;
        }
        set
        {
            _smoothCyclesEffectPercent = SmoothCyclesEffectRange.Clamp(value);
        }
    }

    // How strongly Smooth Cycles evens out the cycles, as the fraction of the whole way to their average.
    public static double SmoothCyclesEffect
    {
        get
        {
            return _smoothCyclesEffectPercent / Percent.PerWhole;
        }
    }
}
