using Avalonia.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// One of the app's own settings chosen on a slider: its name above, and the slider beside a readout of the value it is at, which it reports as it moves.
public sealed class SettingSlider : Decorator
{
    private readonly TextBlock _nameText = new();
    private readonly Slider _slider = new();
    private readonly TextBlock _readout = new();

    // Builds the name above the row of the slider and its readout.
    public SettingSlider()
    {
        _readout.Classes.Add(ThemeClasses.SliderReadout);
        var sliderRow = new StackPanel { Children = { _slider, _readout } };
        sliderRow.Classes.Add(ThemeClasses.SettingRow);
        var setting = new StackPanel { Children = { _nameText, sliderRow } };
        setting.Classes.Add(ThemeClasses.Setting);
        Child = setting;
    }

    // The setting's name, shown above the slider.
    public string? SettingName
    {
        get
        {
            return _nameText.Text;
        }
        set
        {
            _nameText.Text = value;
        }
    }

    // How far apart the ticks under the slider stand, each one a value it snaps to.
    public double TickFrequency
    {
        get
        {
            return _slider.TickFrequency;
        }
        set
        {
            _slider.TickFrequency = value;
        }
    }

    // Where the ticks are drawn beside the slider, if at all.
    public TickPlacement TickPlacement
    {
        get
        {
            return _slider.TickPlacement;
        }
        set
        {
            _slider.TickPlacement = value;
        }
    }

    // Puts the slider over the setting's range at its value and shows that value, then shows and reports each value it moves to, a right-click putting it
    // back at the setting's base value.
    public void Show(ValueRange range, int value, int baseValue, Func<int, string> describe, Action<int> report)
    {
        _slider.Minimum = range.Min;
        _slider.Maximum = range.Max;
        _slider.Value = value;
        _readout.Text = describe((int)_slider.Value);
        _slider.ValueChanged += (_, e) =>
        {
            int movedValue = (int)e.NewValue;
            _readout.Text = describe(movedValue);
            report(movedValue);
        };
        FieldResets.ResetsTo(_slider, () => baseValue);
    }
}
