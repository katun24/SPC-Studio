using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SpcStudio.Controls;

// The field a press gave the focus to on its own, with the text it held then, for as long as it keeps the focus.
public static class PressFocusedField
{
    private static TextBox? _pressFocusedField;
    private static string? _textWhenFocused;

    // Gives a field the focus on a press's behalf and selects what is in it, remembering it and its text until it loses the focus.
    public static void Focus(TextBox box)
    {
        FieldInput.FocusField(box);
        _pressFocusedField = box;
        _textWhenFocused = box.Text;
        box.LostFocus -= ForgetField;
        box.LostFocus += ForgetField;
    }

    // Whether the focus is in a field a press gave it to, still holding the text it held then.
    public static bool IsUntouched(IInputElement? focused)
    {
        return focused is TextBox box && box == _pressFocusedField && box.Text == _textWhenFocused;
    }

    // Stops remembering a field once it loses the focus.
    private static void ForgetField(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        box.LostFocus -= ForgetField;
        if (box == _pressFocusedField)
        {
            _pressFocusedField = null;
        }
    }
}
