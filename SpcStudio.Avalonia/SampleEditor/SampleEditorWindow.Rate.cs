using Avalonia.Controls;
using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The sample editor's Rate row: the rate the sample showing is converted to from its kept audio, which is what it plays, exports and costs ARAM at, as a
// slider in semitones below the kept audio's own rate and the rate in hertz.
public partial class SampleEditorWindow
{
    private const int RateFieldStepHz = 1;

    private Control? _rateControlBeingEdited;
    private int? _askedRateHz;

    // Hangs the slider and the field on the rate of the sample showing: a right-click puts either back on the kept audio's own rate.
    private void WireRateRow()
    {
        RateSlider.ValueChanged += (_, _) => OnRateSliderChanged();
        FieldResets.OnReset(RateSlider, () => ResetRate(RateSlider));
        FieldSteps.HookLiveField(RateHzBox, ApplyTypedRate, StepRate, () => ResetRate(RateHzBox), () => ShowRate(CurrentSample));
    }

    // Writes the rate of a sample into the slider and the field: the one the Rate row last asked for where it has asked for one since the sample was last
    // shown whole, or else the sample's own.
    private void ShowRate(Sample? sample)
    {
        ShowRateAt(sample, sample is null ? null : ShownRateHz(sample));
    }

    // Writes a rate of a sample into the slider, whose rail runs from its lowest rate to its kept audio's own, and the field, leaving alone the control the
    // edit is coming from.
    private void ShowRateAt(Sample? sample, int? rateHz)
    {
        FlagGuard.RunWithFlagSet(ref _isUpdatingControls, () =>
        {
            if (sample is not null)
            {
                SliderField.SetRail(RateSlider, new ValueRange((int)Math.Floor(SemitonesBelowKeptRate(sample, sample.RateRange.Min)), 0));
            }

            if (!ReferenceEquals(_rateControlBeingEdited, RateSlider))
            {
                RateSlider.Value = sample is null || rateHz is not int shownRateHz ? 0 : SliderField.HeldToRail(RateSlider, SemitonesBelowKeptRate(sample, shownRateHz));
            }

            if (_rateControlBeingEdited is TextBox)
            {
                return;
            }

            FieldInput.WriteChangedText(RateHzBox, FieldInput.TextOf(rateHz));
        });
    }

    // Takes the slider's semitones below the kept audio's own rate as the rate.
    private void OnRateSliderChanged()
    {
        if (_isUpdatingControls || CurrentSample is not { } sample)
        {
            return;
        }

        SetRate((int)Math.Round(sample.KeptRateHz * SnesPitch.FrequencyRatioForSemitones(RateSlider.Value)), RateSlider, RateSlider);
    }

    // Takes what the field holds as the rate, where it reads as a rate in the sample's range other than the one showing.
    private void ApplyTypedRate()
    {
        if (_isUpdatingControls || CurrentSample is not { } sample || !FieldInput.TryReadValue(RateHzBox.Text, sample.RateRange, out int rateHz)
            || rateHz == ShownRateHz(sample))
        {
            return;
        }

        SetRate(rateHz, RateHzBox, RateHzBox);
    }

    // Moves the rate showing by a number of the field's steps.
    private void StepRate(int steps)
    {
        if (CurrentSample is not { } sample)
        {
            return;
        }

        SetRate(ShownRateHz(sample) + steps * RateFieldStepHz, RateHzBox);
    }

    // Puts the rate back on the kept audio's own, for a right-click on the slider or the field.
    private void ResetRate(Control source)
    {
        if (CurrentSample is not { } sample)
        {
            return;
        }

        SetRate(sample.KeptRateHz, source);
    }

    // Asks for the sample showing to be converted to a rate held to its range, shows that rate on the row at once but on one control left alone, and what
    // it costs in ARAM there, and leaves nothing selected.
    private void SetRate(int rateHz, Control source, Control? controlToLeaveAlone = null)
    {
        if (_selectedSampleId is not { } id || CurrentSample is not { } sample)
        {
            return;
        }

        int heldRateHz = sample.RateRange.Clamp(rateHz);
        _askedRateHz = heldRateHz;
        FlagGuard.RunWithControlMarked(ref _rateControlBeingEdited, controlToLeaveAlone, () =>
        {
            RateChanged?.Invoke(id, heldRateHz, source);
            ShowRateAt(sample, heldRateHz);
        });
        ShowSize(sample);
        Waveform.Select(0, 0);
    }

    // The rate the row shows for a sample: the one it last asked for where it has asked for one since the sample was last shown whole, or else the
    // sample's own.
    private int ShownRateHz(Sample sample)
    {
        return _askedRateHz ?? sample.RateHz;
    }

    // How many semitones a rate stands below a sample's kept audio's own, as a negative number, fractions included.
    private static double SemitonesBelowKeptRate(Sample sample, int rateHz)
    {
        return SnesPitch.SemitonesForFrequencyRatio(rateHz / (double)sample.KeptRateHz);
    }
}
