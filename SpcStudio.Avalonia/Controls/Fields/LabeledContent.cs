using Avalonia;
using Avalonia.Controls;

namespace SpcStudio.Controls;

// A control with a word of its own drawn beside it, where the word stands being the derived element's.
public abstract class LabeledContent : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<LabeledContent, string?>(nameof(Label));

    // The word drawn beside the control.
    public string? Label
    {
        get
        {
            return GetValue(LabelProperty);
        }
        set
        {
            SetValue(LabelProperty, value);
        }
    }
}
