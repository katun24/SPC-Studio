using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio;

// The sample editor's effects rack: a row for every effect on the sample showing, each change going out to the main window as the sample's new effects.
public partial class SampleEditorWindow
{
    private const double RackNameWidth = 76;
    private const double ReadoutWidth = 64;
    private const int EnableColumn = 0;
    private const int NameColumn = 1;
    private const int FirstSliderColumn = 2;
    private const int ColumnsPerSetting = 2;

    private SampleRack? _rackRowsBuiltFrom;
    private int _rackRowsBuiltAtRateHz;
    private bool _isRackWriting;

    // The effects on the sample showing, or every effect switched off where no sample is showing.
    private SampleRack CurrentEffects
    {
        get
        {
            return EffectsOf(CurrentSample);
        }
    }

    // Gives the window whatever height the section gains or loses.
    private void WireRack()
    {
        RackSection.SizeChanged += OnRackSectionResized;
    }

    // Shows a sample's effects - building the rows again only where they were built from other effects or for another rate - and counts the ones switched
    // on in the banner.
    private void SyncRack(Sample? sample)
    {
        var effects = EffectsOf(sample);
        int rateHz = RateOf(sample);
        if (_rackRowsBuiltFrom is null || !effects.IsSameAs(_rackRowsBuiltFrom) || rateHz != _rackRowsBuiltAtRateHz)
        {
            BuildRackRows(effects, rateHz);
        }

        RackSection.SwitchedOnCount = effects.EnabledCount;
    }

    // The effects on a sample, or every effect switched off where there is no sample.
    private static SampleRack EffectsOf(Sample? sample)
    {
        return sample?.Effects ?? SampleRack.AllOffAt(RateOf(sample));
    }

    // The rate a sample's effects work at, or the DSP's own where there is no sample.
    private static int RateOf(Sample? sample)
    {
        return sample?.RateHz ?? Sample.BrrSourceRateHz;
    }

    // Builds a row per effect for a sample at a rate, the last with no gap under it.
    private void BuildRackRows(SampleRack effects, int rateHz)
    {
        _rackRowsBuiltFrom = effects;
        _rackRowsBuiltAtRateHz = rateHz;
        RackRows.Children.Clear();
        foreach (var effect in effects.Effects)
        {
            RackRows.Children.Add(BuildRackRow(effect, rateHz));
        }

        RackRows.Children[^1].Margin = default;
    }

    // Grows or shrinks the window by what the section just gained or lost, so the waveform keeps its height.
    private void OnRackSectionResized(object? sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged || e.PreviousSize.Height == 0 || WindowState != WindowState.Normal)
        {
            return;
        }

        double currentWindowHeight = double.IsNaN(Height) ? Bounds.Height : Height;
        Height = currentWindowHeight + e.NewSize.Height - e.PreviousSize.Height;
    }

    // One row of the rack for a sample at a rate: the box that switches the effect on, its name, and a slider and a readout per setting its kind declares,
    // the sliders inert while the box is clear.
    private Grid BuildRackRow(SampleEffect effect, int rateHz)
    {
        var kind = effect.Kind;
        var settings = SampleEffect.SettingsOf(kind, rateHz);
        var row = new Grid
        {
            ColumnDefinitions = RackRowColumns(settings),
            Height = ThemeResource.FieldHeight,
            Margin = FieldRows.GapUnderRow,
        };

        var enableBox = new CheckBox
        {
            IsChecked = effect.IsEnabled,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            MinWidth = 0,
        };
        Grid.SetColumn(enableBox, EnableColumn);
        row.Children.Add(enableBox);

        var name = new TextBlock { Text = SampleEffect.NameOf(kind), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.Px6, 0, 0, 0) };
        Grid.SetColumn(name, NameColumn);
        row.Children.Add(name);

        var sliders = new List<Slider>();
        for (int index = 0; index < settings.Count; index++)
        {
            var slider = AddSettingControls(row, kind, index, settings[index], effect.Values[index]);
            slider.AddHandler(PointerPressedEvent, (sender, e) => SwitchOnWhereClaimed(sender, e, kind, enableBox), RoutingStrategies.Tunnel);
            sliders.Add(slider);
        }

        ShowSlidersLive(sliders, effect.IsEnabled);
        enableBox.IsCheckedChanged += (_, _) =>
        {
            bool isEnabled = enableBox.IsChecked == true;
            ShowSlidersLive(sliders, isEnabled);
            if (CurrentEffects.EffectOf(kind).IsEnabled == isEnabled)
            {
                return;
            }

            WriteEffects(CurrentEffects.WithEnabled(kind, isEnabled), enableBox);
        };
        return row;
    }

    // A rack row's columns: the switch, the name, and a slider and a readout per setting.
    private static ColumnDefinitions RackRowColumns(IReadOnlyList<SampleSetting> settings)
    {
        var columns = new ColumnDefinitions
        {
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(RackNameWidth, GridUnitType.Pixel),
        };
        for (int index = 0; index < settings.Count; index++)
        {
            columns.Add(new ColumnDefinition(GridLength.Star));
            columns.Add(new ColumnDefinition(ReadoutWidth, GridUnitType.Pixel));
        }

        return columns;
    }

    // Adds one setting's slider and readout to a rack row, the slider writing the setting through the rack as it moves, and hands back the slider.
    private Slider AddSettingControls(Grid row, SampleEffectKind kind, int settingIndex, SampleSetting setting, double value)
    {
        var readout = new TextBlock
        {
            Text = ReadoutTextOf(setting, value),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = ThemeResource.TextSecondaryBrush,
        };
        var slider = new Slider
        {
            Minimum = SliderPositionOf(setting, setting.Minimum),
            Maximum = SliderPositionOf(setting, setting.Maximum),
            Value = SliderPositionOf(setting, value),
            TickFrequency = setting.IsWholeNumber ? 1 : 0,
            IsSnapToTickEnabled = setting.IsWholeNumber,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Space.Px9, 0, Space.Px9, 0),
        };
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty)
            {
                return;
            }

            double settingValue = SettingValueAt(setting, slider);
            readout.Text = ReadoutTextOf(setting, settingValue);
            WriteEffects(CurrentEffects.WithReplaced(CurrentEffects.EffectOf(kind).WithValue(settingIndex, settingValue)), slider);
        };
        FieldResets.ResetsTo(slider, () => SliderPositionOf(setting, setting.DefaultValue));

        // The column the setting's slider stands in, its readout standing in the next.
        int sliderColumn = FirstSliderColumn + settingIndex * ColumnsPerSetting;
        Grid.SetColumn(slider, sliderColumn);
        Grid.SetColumn(readout, sliderColumn + 1);
        row.Children.Add(slider);
        row.Children.Add(readout);
        return slider;
    }

    // Makes a row's sliders live where its effect is switched on, and inert and out of the tab order where it is off.
    private static void ShowSlidersLive(IEnumerable<Slider> sliders, bool isEnabled)
    {
        foreach (var slider in sliders)
        {
            FieldSteps.SetInertOutOfTabOrder(slider, !isEnabled);
        }
    }

    // Switches a row's effect on and ticks its box where a press landed on its inert slider and claims it, as a claim the drag the press goes on to make joins.
    private void SwitchOnWhereClaimed(object? sender, PointerPressedEventArgs e, SampleEffectKind kind, CheckBox enableBox)
    {
        if (!FieldSteps.IsClaimingPress(sender, e))
        {
            return;
        }

        WriteEffects(CurrentEffects.WithEnabled(kind, true), EditStep.ClaimSource);
        enableBox.IsChecked = true;
    }

    // A setting's value as its readout writes it, with its unit where it has one.
    private static string ReadoutTextOf(SampleSetting setting, double value)
    {
        string number = value.ToString(setting.Format);
        return setting.Unit.Length > 0 ? $"{number} {setting.Unit}" : number;
    }

    // Where on its slider a setting's value sits: the value itself, or its logarithm for a setting whose travel is in doublings.
    private static double SliderPositionOf(SampleSetting setting, double value)
    {
        return setting.IsLogarithmic ? Math.Log(value) : value;
    }

    // What a slider's position means as its setting's value, either end handed back exactly.
    private static double SettingValueAt(SampleSetting setting, Slider slider)
    {
        if (slider.Value <= slider.Minimum)
        {
            return setting.Minimum;
        }

        if (slider.Value >= slider.Maximum)
        {
            return setting.Maximum;
        }

        return setting.IsLogarithmic ? Math.Exp(slider.Value) : slider.Value;
    }

    // Sends the sample showing's new effects out to the main window as an edit from a row's own control, taking the rows as built from them so that row stays standing.
    private void WriteEffects(SampleRack effects, object editSource)
    {
        if (_selectedSampleId is not { } id)
        {
            return;
        }

        _rackRowsBuiltFrom = effects;
        FlagGuard.RunWithFlagSet(ref _isRackWriting, () => EffectsChanged?.Invoke(id, effects, editSource));
    }
}
