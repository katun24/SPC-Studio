using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// The three DSP envelope bytes of one envelope as a set of controls - ADSR or GAIN, and four fields or a shape and one - each edit reported as a change to apply to the envelope.
public partial class EnvelopeEditor : UserControl
{
    private const int DirectLevelWordIndex = 0;
    private const int SlopeRateWordIndex = 1;
    private const int FieldMinValue = 0;

    private static readonly string[] GainValueRowLabelWords = ["Level", "Rate"];
    private static readonly (GainMode Mode, string Label)[] GainShapeChoices =
    [
        (GainMode.Direct, "Flat"),
        (GainMode.LinearDecrease, "Linear decrease"),
        (GainMode.ExponentialDecrease, "Exponential decrease"),
        (GainMode.LinearIncrease, "Linear increase"),
        (GainMode.BentIncrease, "Bent increase"),
    ];

    private readonly EnvelopeField[] _fields;
    private EnvelopeState _envelope;
    private bool _isUpdating;
    private TextBox? _fieldBeingTyped;

    // Builds the controls, ties each slider to its field and gives every field the arrows and the wheel.
    public EnvelopeEditor()
    {
        InitializeComponent();

        GainShapeBox.ItemsSource = GainShapeChoices.Select(gainMode => gainMode.Label).ToArray();

        _fields =
        [
            new EnvelopeField(AttackBox, AttackSlider, _ => EnvelopeState.MaxAttackRate, state => state.AttackRate, (state, value) => state.WithAttackRate(value)),
            new EnvelopeField(DecayBox, DecaySlider, _ => EnvelopeState.MaxDecayRate, state => state.DecayRate, (state, value) => state.WithDecayRate(value)),
            new EnvelopeField(SustainLevelBox, SustainLevelSlider, _ => EnvelopeState.MaxSustainLevel, state => state.SustainLevel,
                (state, value) => state.WithSustainLevel(value)),
            new EnvelopeField(SustainRateBox, SustainRateSlider, _ => EnvelopeState.MaxSustainRate, state => state.SustainRate,
                (state, value) => state.WithSustainRate(value)),
            new EnvelopeField(GainValueBox, GainValueSlider, state => state.MaxGainValue, state => state.GainValue, (state, value) => state.WithGainValue(value)),
        ];

        foreach (var field in _fields)
        {
            SliderField.Tie(field.Slider, field.Box, field.RangeIn(_envelope));
            FieldSteps.OnKeys(field.Box, OnFieldKeyDown);
            FieldResets.OnReset(field.Box, () => RequestReset(field));
        }
    }

    // Raised when one of the bytes is moved: the control the edit came from, and the change to apply to the envelope.
    public event Action<object, Func<EnvelopeState, EnvelopeState>>? Edited;

    // Raised by a right-click on a field: the field, and how the envelope takes that field's value from the envelope it is based on.
    public event Action<object, Func<EnvelopeState, EnvelopeState, EnvelopeState>>? ResetRequested;

    // Every control an edit is made with: the two radio buttons, the GAIN shape, and each field and its slider.
    public IEnumerable<Control> EditingControls
    {
        get
        {
            return [AdsrModeButton, GainModeButton, GainShapeBox, .. _fields.SelectMany(field => new Control[] { field.Box, field.Slider })];
        }
    }

    // Points the editor at an envelope.
    public void Show(EnvelopeState envelope)
    {
        _envelope = envelope;
        FlagGuard.RunWithFlagSet(ref _isUpdating, ShowEnvelopeControls);
    }

    // Writes the mode, the GAIN shape and every field but the one being typed into from the envelope.
    private void ShowEnvelopeControls()
    {
        AdsrModeButton.IsChecked = _envelope.UsesAdsr;
        GainModeButton.IsChecked = !_envelope.UsesAdsr;
        AdsrFields.IsVisible = _envelope.UsesAdsr;
        GainFields.IsVisible = !_envelope.UsesAdsr;

        GainShapeBox.SelectedIndex = Array.FindIndex(GainShapeChoices, entry => entry.Mode == _envelope.GainMode);
        GainValueRow.Label = GainValueRowLabelWords[_envelope.GainMode == GainMode.Direct ? DirectLevelWordIndex : SlopeRateWordIndex];
        GainValueSlider.Maximum = _envelope.MaxGainValue;

        foreach (var field in _fields)
        {
            if (ReferenceEquals(field.Box, _fieldBeingTyped))
            {
                continue;
            }

            field.Box.Text = FieldInput.TextOf(field.Read(_envelope));
        }
    }

    // Reports a change, holding the field it came from as the one whose text is not rewritten while the owner shows the editor again.
    private void ReportEdit(object source, Func<EnvelopeState, EnvelopeState> change)
    {
        _fieldBeingTyped = source as TextBox;
        try
        {
            Edited?.Invoke(source, change);
        }
        finally
        {
            _fieldBeingTyped = null;
        }
    }

    // Switches the envelope to ADSR or to GAIN, whichever radio button is now on.
    private void OnModeChanged(object? sender, RoutedEventArgs e)
    {
        if (_isUpdating)
        {
            return;
        }

        bool usesAdsr = AdsrModeButton.IsChecked == true;
        if (_envelope.UsesAdsr == usesAdsr)
        {
            return;
        }

        ReportEdit(sender!, state => state.WithUsesAdsr(usesAdsr));
    }

    // Gives the envelope the GAIN shape picked.
    private void OnGainShapeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating || GainShapeBox.SelectedIndex < 0)
        {
            return;
        }

        var gainMode = GainShapeChoices[GainShapeBox.SelectedIndex].Mode;
        if (_envelope.GainMode == gainMode)
        {
            return;
        }

        ReportEdit(sender!, state => state.WithGainMode(gainMode));
    }

    // Applies a field as it is typed, where its text is a value the field's range holds.
    private void OnFieldChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdating)
        {
            return;
        }

        var field = FieldOf((TextBox)sender!);
        if (!FieldInput.TryReadValue(field.Box.Text, field.RangeIn(_envelope), out int value))
        {
            return;
        }

        ReportEdit(field.Box, state => field.Write(state, value));
    }

    // Steps a field by an arrow key through its text, held to its range.
    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (!FieldSteps.TryTakeStep(e, out int step))
        {
            return;
        }

        var field = FieldOf((TextBox)sender!);
        FieldInput.WriteStepped(field.Box, field.Read(_envelope), step, field.RangeIn(_envelope));
    }

    // Asks the host to put one field of the envelope back at the value its base envelope holds.
    private void RequestReset(EnvelopeField field)
    {
        if (_isUpdating)
        {
            return;
        }

        ResetRequested?.Invoke(field.Box, (baseEnvelope, envelope) => field.Write(envelope, field.Read(baseEnvelope)));
    }

    // Puts the envelope's value back into a field when it loses the focus, touching no other field.
    private void OnFieldCommitted(object? sender, RoutedEventArgs e)
    {
        var field = FieldOf((TextBox)sender!);
        FlagGuard.RunWithFlagSet(ref _isUpdating, () => field.Box.Text = FieldInput.TextOf(field.Read(_envelope)));
    }

    // The field a box is.
    private EnvelopeField FieldOf(TextBox box)
    {
        return _fields.First(field => ReferenceEquals(field.Box, box));
    }

    // One of the five fields: its box and slider, the largest value it holds for an envelope, and how it reads and writes one.
    private sealed record EnvelopeField(
        TextBox Box, Slider Slider, Func<EnvelopeState, int> Max, Func<EnvelopeState, int> Read, Func<EnvelopeState, int, EnvelopeState> Write)
    {
        // The values the field holds for an envelope.
        public ValueRange RangeIn(EnvelopeState envelope)
        {
            return new(FieldMinValue, Max(envelope));
        }
    }
}
