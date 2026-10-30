namespace SpcStudio.Controls;

// What a key press over the waveform or an item of the Sample Editor's Edit menu asks for.
public enum SampleEditKind
{
    Delete,
    Copy,
    Cut,
    Paste,
    Trim,
    FadeIn,
    FadeOut,
    Reverse,
    SelectLoop,
    FindLoop,
    FlattenVolume,
    SmoothCycles,
    ConvertRate,
}
