namespace SpcStudio.Audio;

// One setting of one effect, which is one slider on its row: its name and unit, its range, where a fresh row starts, how its readout is written,
// whether the slider moves through it in doublings, and whether it holds whole numbers only.
public sealed record SampleSetting(
    string Name,
    string Unit,
    double Minimum,
    double Maximum,
    double DefaultValue,
    string Format,
    bool IsLogarithmic,
    bool IsWholeNumber);
