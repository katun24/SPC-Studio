using Avalonia.Controls;
using Avalonia.Interactivity;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// A numeric value row's parts: its slider, the field the slider writes, its rung switch and the range of the value it holds.
public sealed record NumericRowParts(Slider Slider, TextBox Box, FieldOverride Switch, ValueRange Range)
{
    // Ties the slider to the field over the row's range.
    public void TieSliderToBox()
    {
        SliderField.Tie(Slider, Box, Range);
    }

    // Runs one handler as the field is typed into and another when it loses the focus.
    public void OnTypedOrLeft(EventHandler<TextChangedEventArgs> typed, EventHandler<RoutedEventArgs> left)
    {
        Box.TextChanged += typed;
        Box.LostFocus += left;
    }
}
