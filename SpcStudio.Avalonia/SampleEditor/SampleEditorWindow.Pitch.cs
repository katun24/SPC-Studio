using Avalonia.Controls;
using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The sample editor's Pitch row: the PITCH register the preview plays the sample showing at, as a slider in semitones from the DSP's own rate, the note
// and the cents the sample sounds at there, and the rate in hertz - one value shown four ways, which changes nothing about the audio.
public partial class SampleEditorWindow
{
    private const int HalfSemitoneCents = SnesPitch.CentsPerSemitone / 2;
    private const int CentsFieldStepCents = 1;

    private static readonly ValueRange CentsFieldRange = new(-HalfSemitoneCents, HalfSemitoneCents);

    // The rates in whole hertz the Hz field takes: those the preview's range of PITCH registers plays at.
    private static readonly ValueRange HzFieldRange = new((int)Math.Ceiling(HzOfPitchRegister(Sample.PreviewPitchRegisterRange.Min)),
        (int)Math.Floor(HzOfPitchRegister(Sample.PreviewPitchRegisterRange.Max)));

    private Control? _pitchControlBeingEdited;

    // Hangs the slider and the three fields on the rate of the sample showing: a right-click puts the slider, Tone and Cents back on the sample's own C4,
    // and the Hz field back on the DSP's own rate.
    private void WirePitchRow()
    {
        var registerRange = Sample.PreviewPitchRegisterRange;
        int lowestSemitone = (int)Math.Floor(SemitonesFromNativeRate(registerRange.Min));
        int highestSemitone = (int)Math.Ceiling(SemitonesFromNativeRate(registerRange.Max));
        SliderField.SetRail(PitchSlider, new ValueRange(lowestSemitone, highestSemitone));
        PitchSlider.ValueChanged += (_, _) => OnPitchSliderChanged();
        FieldResets.OnReset(PitchSlider, () => SetPitch(null, PitchSlider));

        HookPitchField(PitchToneBox, PitchRegisterTypedAsTone, (sample, steps) => PitchRegisterSteppedByCents(sample, steps, SnesPitch.CentsPerSemitone), null);
        HookPitchField(PitchCentsBox, PitchRegisterTypedAsCents, (sample, steps) => PitchRegisterSteppedByCents(sample, steps, CentsFieldStepCents), null);
        HookPitchField(PitchHzBox, PitchRegisterTypedAsHz, (sample, steps) => sample.PreviewPitchRegister + steps, SnesPitch.UnityPitchRegister);
    }

    // Hangs one pitch field on the rate: it applies as typed, steps as its own step says, goes back to its base on a right-click - a register, or the
    // sample's own C4 where it is null - and puts the tidy spelling back on Enter and on losing the focus.
    private void HookPitchField(TextBox box, Func<Sample, string?, int?> pitchRegisterTyped, Func<Sample, int, int> pitchRegisterStepped, int? basePitchRegister)
    {
        box.TextChanged += (_, _) => ApplyTypedPitch(box, pitchRegisterTyped);
        box.LostFocus += (_, _) => ShowPitch(CurrentSample);
        FieldResets.OnReset(box, () => SetPitch(basePitchRegister, box));
        FieldSteps.OnStepsAndEnter(box, steps => StepPitch(box, pitchRegisterStepped, steps), () => ShowPitch(CurrentSample));
    }

    // Writes the rate of a sample into the slider and the three fields, leaving alone the control the edit is coming from; empties them and disables the
    // row where no sample shows.
    private void ShowPitch(Sample? sample)
    {
        FlagGuard.RunWithFlagSet(ref _isUpdatingControls, () =>
        {
            if (!ReferenceEquals(_pitchControlBeingEdited, PitchSlider))
            {
                PitchSlider.Value = sample is null ? 0 : SliderField.HeldToRail(PitchSlider, SemitonesFromNativeRate(sample.PreviewPitchRegister));
            }

            if (_pitchControlBeingEdited is TextBox)
            {
                return;
            }

            var sounded = sample is null ? null : SoundedPitchOnRoll(sample);
            FieldInput.WriteChangedText(PitchToneBox, sounded is { } toneShown ? PitchLabel.NameOf(toneShown.Semitone) : "");
            FieldInput.WriteChangedText(PitchCentsBox, FieldInput.TextOf(sounded?.Cents));
            FieldInput.WriteChangedText(PitchHzBox, FieldInput.TextOf(sample is null ? null : HzShownOf(sample)));
        });
    }

    // Takes the slider's semitones from the DSP's own rate as the rate.
    private void OnPitchSliderChanged()
    {
        if (_isUpdatingControls)
        {
            return;
        }

        SetPitch((int)Math.Round(SnesPitch.UnityPitchRegister * SnesPitch.FrequencyRatioForSemitones(PitchSlider.Value)), PitchSlider, PitchSlider);
    }

    // Takes what a pitch field holds as the rate, where it reads as one.
    private void ApplyTypedPitch(TextBox box, Func<Sample, string?, int?> pitchRegisterTyped)
    {
        if (_isUpdatingControls || CurrentSample is not { } sample || pitchRegisterTyped(sample, box.Text) is not int pitchRegister)
        {
            return;
        }

        SetPitch(pitchRegister, box, box);
    }

    // Moves the rate by a number of a field's steps.
    private void StepPitch(TextBox source, Func<Sample, int, int> pitchRegisterStepped, int steps)
    {
        if (CurrentSample is not { } sample)
        {
            return;
        }

        SetPitch(pitchRegisterStepped(sample, steps), source);
    }

    // Asks for the sample showing to be played at a PITCH register, or at its own C4 where the register is null, leaving one control alone in the redraw
    // that follows.
    private void SetPitch(int? pitchRegister, Control source, Control? controlToLeaveAlone = null)
    {
        if (_selectedSampleId is not { } id)
        {
            return;
        }

        FlagGuard.RunWithControlMarked(ref _pitchControlBeingEdited, controlToLeaveAlone, () => PreviewPitchChanged?.Invoke(id, pitchRegister, source));
    }

    // The register a sample's rate moves to by a number of steps of an interval in cents from the cents its fields show, or by one register step a
    // step where the interval is finer than that.
    private static int PitchRegisterSteppedByCents(Sample sample, int steps, int stepCents)
    {
        int pitchRegister = sample.PreviewPitchRegister;
        double centsAboveC4 = SnesPitch.SemitonesAboveC4At(pitchRegister, sample.C4Tuning) * SnesPitch.CentsPerSemitone;
        return SnesPitch.CountSteppedByCents(pitchRegister, centsAboveC4, steps, stepCents);
    }

    // The register a note typed into the Tone field asks for, the cents the sample sounds off its note kept, or null where the text names no note on the
    // roll or names the note already sounding.
    private static int? PitchRegisterTypedAsTone(Sample sample, string? text)
    {
        var sounded = SoundedPitchOf(sample);
        if (!PitchLabel.TryParse(text ?? "", out int semitone) || semitone == sounded.Semitone)
        {
            return null;
        }

        return PitchRegisterSounding(sample, sounded with { Semitone = semitone });
    }

    // The register a number typed into the Cents field asks for, on the note the sample sounds at, or null where the text is not a number in the field's
    // range or is the cents already sounding.
    private static int? PitchRegisterTypedAsCents(Sample sample, string? text)
    {
        var sounded = SoundedPitchOf(sample);
        if (!FieldInput.TryReadSigned(text, CentsFieldRange, out int cents) || cents == sounded.Cents)
        {
            return null;
        }

        return PitchRegisterSounding(sample, sounded with { Cents = cents });
    }

    // The register nearest a rate typed into the Hz field, or null where the text is not a rate in the field's range or is the rate already showing.
    private static int? PitchRegisterTypedAsHz(Sample sample, string? text)
    {
        if (!FieldInput.TryReadValue(text, HzFieldRange, out int hz) || hz == HzShownOf(sample))
        {
            return null;
        }

        return (int)Math.Round(hz * SnesPitch.UnityPitchRegister / DspPoint.SampleRate);
    }

    // The register that sounds a sample at a note and a number of cents off it.
    private static int PitchRegisterSounding(Sample sample, SoundedPitch pitch)
    {
        return SnesPitch.PitchRegisterAt(pitch.Semitone + pitch.Cents / (double)SnesPitch.CentsPerSemitone, sample.C4Tuning);
    }

    // The note the preview sounds a sample at and the whole cents off it, from half a semitone below the note to just under half a semitone above.
    private static SoundedPitch SoundedPitchOf(Sample sample)
    {
        double semitonesAboveC4 = SnesPitch.SemitonesAboveC4At(sample.PreviewPitchRegister, sample.C4Tuning);
        int centsAboveC4 = (int)Math.Round(semitonesAboveC4 * SnesPitch.CentsPerSemitone);
        int semitone = (int)Math.Floor((centsAboveC4 + HalfSemitoneCents) / (double)SnesPitch.CentsPerSemitone);
        return new SoundedPitch(semitone, centsAboveC4 - semitone * SnesPitch.CentsPerSemitone);
    }

    // The pitch the preview sounds a sample at, or null where its note is off the roll and has no name to show.
    private static SoundedPitch? SoundedPitchOnRoll(Sample sample)
    {
        var sounded = SoundedPitchOf(sample);
        return Song.SemitoneRange.Contains(sounded.Semitone) ? sounded : null;
    }

    // How many semitones a PITCH register plays a sample above the DSP's own rate, fractions included.
    private static double SemitonesFromNativeRate(int pitchRegister)
    {
        return SnesPitch.SemitonesForFrequencyRatio(SnesPitch.RateOf(pitchRegister));
    }

    // The rate in whole hertz the Hz field shows for a sample.
    private static int HzShownOf(Sample sample)
    {
        return (int)Math.Round(HzOfPitchRegister(sample.PreviewPitchRegister));
    }

    // The rate in hertz a PITCH register plays a sample's points at.
    private static double HzOfPitchRegister(int pitchRegister)
    {
        return SnesPitch.RateOf(pitchRegister) * DspPoint.SampleRate;
    }

    // A note and the whole cents off it.
    private readonly record struct SoundedPitch(int Semitone, int Cents);
}
