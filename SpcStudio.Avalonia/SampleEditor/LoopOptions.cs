using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio;

// The Sample Editor's Loop Options, which the app remembers between sessions: which loop Find Loop takes and where it may start, which steps it runs over the loop it found,
// how strongly Smooth Cycles evens out the cycles it is given, how much of a loop Crossfade Loop blends and how far fitting a loop may lower the rate.
public static class LoopOptions
{
    public const bool DefaultIsShortestLoopPreferred = true;
    public const bool DefaultDoesFindLoopTryStartsBetweenBlocks = false;
    public const bool DefaultDoesFindLoopFitToBlocks = true;
    public const bool DefaultDoesFindLoopFlattenVolume = false;
    public const bool DefaultDoesFindLoopCrossfade = true;
    public const bool DefaultDoesFindLoopSmoothCycles = true;
    public const int DefaultSmoothCyclesEffectPercent = WholePercent;
    public const int DefaultCrossfadeLengthPercent = 50;
    public const int DefaultFitLoopMaxRateLossPercent = 10;
    private const int WholePercent = (int)Percent.PerWhole;
    private const int LeastSliderPercent = 1;
    private const int MostRateLossPercent = 50;

    private static readonly ValueRange SliderPercentRange = new(LeastSliderPercent, WholePercent);
    private static readonly ValueRange RateLossPercentRange = new(LeastSliderPercent, MostRateLossPercent);

    public static bool IsShortestLoopPreferred { get; set; } = DefaultIsShortestLoopPreferred;
    public static bool DoesFindLoopTryStartsBetweenBlocks { get; set; } = DefaultDoesFindLoopTryStartsBetweenBlocks;
    public static bool DoesFindLoopFitToBlocks { get; set; } = DefaultDoesFindLoopFitToBlocks;
    public static bool DoesFindLoopFlattenVolume { get; set; } = DefaultDoesFindLoopFlattenVolume;
    public static bool DoesFindLoopCrossfade { get; set; } = DefaultDoesFindLoopCrossfade;
    public static bool DoesFindLoopSmoothCycles { get; set; } = DefaultDoesFindLoopSmoothCycles;

    // How strongly Smooth Cycles evens out the cycles, in percent of the whole way to their average.
    public static PercentSetting SmoothCyclesEffect { get; } = new(SliderPercentRange, DefaultSmoothCyclesEffectPercent);

    // How much of a loop's end Crossfade Loop blends, in percent of the loop's length.
    public static PercentSetting CrossfadeLength { get; } = new(SliderPercentRange, DefaultCrossfadeLengthPercent);

    // How much lower than the sample's rate fitting a loop to whole blocks may take it, in percent of that rate.
    public static PercentSetting FitLoopMaxRateLoss { get; } = new(RateLossPercentRange, DefaultFitLoopMaxRateLossPercent);

    // How many points apart the loop starts Find Loop tries are: every point where Try Loop Starts Between Blocks is ticked, every block start where not.
    public static int FindLoopStartStepPoints
    {
        get
        {
            return DoesFindLoopTryStartsBetweenBlocks ? LoopFinder.EveryPointStepPoints : LoopFinder.BlockStartStepPoints;
        }
    }
}
