namespace SpcStudio;

// The Sample Editor's Loop Options, each applied as it is changed and reported by event rather than written anywhere by the box itself.
public partial class LoopOptionsWindow : SettingsWidthDialogWindow
{
    // Raised as the Prefer Shortest Loop box is ticked or cleared.
    public event Action<bool>? ShortestLoopPreferredChanged;

    // Raised as the Automatically Flatten Volume box is ticked or cleared.
    public event Action<bool>? FindLoopFlattenVolumeChanged;

    // Raised as the Automatically Smooth Cycles box is ticked or cleared.
    public event Action<bool>? FindLoopSmoothCyclesChanged;

    // Raised with the Smooth Cycles effect in percent, as its slider moves.
    public event Action<int>? SmoothCyclesEffectChanged;

    // Builds the box.
    public LoopOptionsWindow()
    {
        InitializeComponent();
    }

    // Builds the box showing the settings given, the slider held to the range the effect takes.
    public LoopOptionsWindow(bool isShortestLoopPreferred, bool doesFindLoopFlattenVolume, bool doesFindLoopSmoothCycles, int smoothCyclesEffectPercent) : this()
    {
        ShowCheckBox(ShortestLoopBox, isShortestLoopPreferred, isPreferred => ShortestLoopPreferredChanged?.Invoke(isPreferred));
        ShowCheckBox(FlattenVolumeBox, doesFindLoopFlattenVolume, doesFlatten => FindLoopFlattenVolumeChanged?.Invoke(doesFlatten));
        ShowCheckBox(SmoothCyclesBox, doesFindLoopSmoothCycles, doesSmooth => FindLoopSmoothCyclesChanged?.Invoke(doesSmooth));
        SmoothCyclesEffectSetting.Show(LoopOptions.SmoothCyclesEffectRange, smoothCyclesEffectPercent, LoopOptions.DefaultSmoothCyclesEffectPercent,
            percent => Percent.Text(percent), percent => SmoothCyclesEffectChanged?.Invoke(percent));
    }
}
