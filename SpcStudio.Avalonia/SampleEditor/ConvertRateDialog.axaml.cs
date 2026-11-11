using Avalonia.Controls;
using Avalonia.Interactivity;
using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The Sample Editor's Convert Rate box: a field in hertz holding the rate a sample is to be converted to, answering it.
public partial class ConvertRateDialog : DialogWindow
{
    private const int RateStepHz = 1;

    private readonly BoundedField _rateField;

    // Builds the box over the DSP's own rate alone.
    public ConvertRateDialog() : this(new ValueRange(Sample.BrrSourceRateHz, Sample.BrrSourceRateHz))
    {
    }

    // Builds the box over the rates a sample may be converted to, the field starting at the highest, which is the sample's own.
    private ConvertRateDialog(ValueRange conversionRateRange)
    {
        InitializeComponent();
        _rateField = BoundedField.ForNumber(RateHzBox, conversionRateRange, conversionRateRange.Max, () => RateStepHz);
    }

    // Shows the box over a window for a sample, with the caret in the field, and answers the rate to convert it to, or null where it was cancelled.
    public static Task<int?> Show(Window owner, Sample sample)
    {
        var dialog = new ConvertRateDialog(sample.ConversionRateRange);
        dialog.Opened += (_, _) => FieldInput.FocusField(dialog.RateHzBox);
        return dialog.ShowDialog<int?>(owner);
    }

    // Closes the box answering the rate the field holds.
    private void OnConvertClick(object? sender, RoutedEventArgs e)
    {
        Close(_rateField.Value);
    }
}
