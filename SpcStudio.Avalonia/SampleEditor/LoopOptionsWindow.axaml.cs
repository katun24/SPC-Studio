using Avalonia.Controls;
using SpcStudio.Controls;

namespace SpcStudio;

// The Sample Editor's Loop Options, each applied as it is changed and reported by event rather than written anywhere by the box itself.
public partial class LoopOptionsWindow : DialogWindow
{
    private const int SliderStepPercent = 1;

    // Raised as the Prefer Shortest Loop box is ticked or cleared.
    public event Action<bool>? ShortestLoopPreferredChanged;

    // Raised as the Try Loop Starts Between Blocks box is ticked or cleared.
    public event Action<bool>? FindLoopStartsBetweenBlocksChanged;

    // Raised as the Fit Loop To Blocks box is ticked or cleared.
    public event Action<bool>? FindLoopFitToBlocksChanged;

    // Raised as the Flatten Volume box is ticked or cleared.
    public event Action<bool>? FindLoopFlattenVolumeChanged;

    // Raised as the Crossfade Loop box is ticked or cleared.
    public event Action<bool>? FindLoopCrossfadeChanged;

    // Raised as the Smooth Cycles box is ticked or cleared.
    public event Action<bool>? FindLoopSmoothCyclesChanged;

    // Raised with the Smooth Cycles effect in percent, as its slider moves.
    public event Action<int>? SmoothCyclesEffectChanged;

    // Raised with the crossfade's length in percent of the loop, as its slider moves.
    public event Action<int>? CrossfadeLengthChanged;

    // Raised with how far fitting a loop may lower the rate, in percent, as its slider moves.
    public event Action<int>? FitLoopMaxRateLossChanged;

    // Builds the box.
    public LoopOptionsWindow()
    {
        InitializeComponent();
    }

    // Builds the box showing the settings given, each slider held to its setting's range.
    public LoopOptionsWindow(bool isShortestLoopPreferred, bool doesFindLoopTryStartsBetweenBlocks, bool doesFindLoopFitToBlocks, bool doesFindLoopFlattenVolume,
        bool doesFindLoopCrossfade, bool doesFindLoopSmoothCycles, PercentSetting smoothCyclesEffect, PercentSetting crossfadeLength, PercentSetting fitLoopMaxRateLoss)
        : this()
    {
        ShowCheckBox(ShortestLoopBox, isShortestLoopPreferred, isPreferred => ShortestLoopPreferredChanged?.Invoke(isPreferred));
        ShowCheckBox(StartsBetweenBlocksBox, doesFindLoopTryStartsBetweenBlocks, doesTry => FindLoopStartsBetweenBlocksChanged?.Invoke(doesTry));
        ShowCheckBox(FitToBlocksBox, doesFindLoopFitToBlocks, doesFit => FindLoopFitToBlocksChanged?.Invoke(doesFit));
        ShowCheckBox(FlattenVolumeBox, doesFindLoopFlattenVolume, doesFlatten => FindLoopFlattenVolumeChanged?.Invoke(doesFlatten));
        ShowCheckBox(CrossfadeBox, doesFindLoopCrossfade, doesCrossfade => FindLoopCrossfadeChanged?.Invoke(doesCrossfade));
        ShowCheckBox(SmoothCyclesBox, doesFindLoopSmoothCycles, doesSmooth => FindLoopSmoothCyclesChanged?.Invoke(doesSmooth));
        ShowPercentSetting(SmoothCyclesEffectSetting, smoothCyclesEffect, percent => SmoothCyclesEffectChanged?.Invoke(percent));
        ShowPercentSetting(CrossfadeLengthSetting, crossfadeLength, percent => CrossfadeLengthChanged?.Invoke(percent));
        ShowPercentSetting(FitLoopMaxRateLossSetting, fitLoopMaxRateLoss, percent => FitLoopMaxRateLossChanged?.Invoke(percent));
    }

    // Puts a slider over a percentage setting's range at its value, stepping by whole percents with no ticks drawn and its readout a percentage, reporting
    // each value it moves to.
    private static void ShowPercentSetting(SettingSlider slider, PercentSetting setting, Action<int> report)
    {
        slider.TickFrequency = SliderStepPercent;
        slider.TickPlacement = TickPlacement.None;
        slider.Show(setting.Range, setting.Percentage, setting.DefaultPercentage, percent => Percent.Text(percent), report);
    }
}
