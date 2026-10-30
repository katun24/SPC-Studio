using System.Globalization;
using Avalonia.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// Ties a panel row's slider to the field beside it: the slider writes the field's text, and the field's text sets the slider.
public static class SliderField
{
    // Puts the values a slider stands over on it, which it draws in place of its handle where they disagree.
    public static void ShowSpan(Slider slider, IEnumerable<int>? values)
    {
        SliderSpan.SetValues(slider, values?.ToArray());
    }

    // A value held to a slider's own range.
    public static double HeldToRail(Slider slider, double value)
    {
        return Math.Clamp(value, slider.Minimum, slider.Maximum);
    }

    // Sets the range a slider runs over.
    public static void SetRail(Slider slider, ValueRange range)
    {
        slider.Minimum = range.Min;
        slider.Maximum = range.Max;
    }

    // Wires one row: the slider's range, its writes into the field, the field's text back onto it, and the wheel and the press over a mixed slider.
    public static void Tie(Slider slider, TextBox box, ValueRange range)
    {
        SetRail(slider, range);

        bool isShowingField = false;

        slider.ValueChanged += (_, _) => WriteSliderIntoField();
        box.TextChanged += (_, _) => ShowFieldOnSlider();
        FieldSteps.PairSliderWithField(slider, box);
        slider.PointerPressed += (_, _) => EndMixedOnPress();

        ShowFieldOnSlider();

        // Writes the slider's position into the field, as a whole number.
        void WriteSliderIntoField()
        {
            if (isShowingField)
            {
                return;
            }

            FieldInput.WriteChangedText(box, FieldInput.TextOf((int)Math.Round(slider.Value)));
        }

        // Sets the slider from the field's text: the number where there is one, and the rail with no handle where the field is blank.
        void ShowFieldOnSlider()
        {
            isShowingField = true;
            try
            {
                if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    slider.Classes.Set(ThemeClasses.Mixed, true);
                    return;
                }

                slider.Value = HeldToRail(slider, value);
                slider.Classes.Remove(ThemeClasses.Mixed);
                SliderSpan.SetValues(slider, null);
            }
            finally
            {
                isShowingField = false;
            }
        }

        // Brings the handle back on the press that ends the mixed state, writing the pressed value into the field.
        void EndMixedOnPress()
        {
            if (!slider.Classes.Contains(ThemeClasses.Mixed))
            {
                return;
            }

            slider.Classes.Remove(ThemeClasses.Mixed);
            WriteSliderIntoField();
        }
    }
}
