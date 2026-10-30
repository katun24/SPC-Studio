using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SpcStudio.Controls;

// What makes a text field a rename field: Enter or the field losing the focus finishes the rename and Escape drops it, the old name staying - and a
// rename field is the one text field that keeps Escape from closing its window.
public static class RenameField
{
    private const string IsRenameFieldPropertyName = "IsRenameField";

    // Whether a text field is a rename field, which wiring it marks.
    private static readonly AttachedProperty<bool> IsRenameFieldProperty = AvaloniaProperty.RegisterAttached<TextBox, bool>(IsRenameFieldPropertyName, typeof(RenameField));

    // Makes a text field a rename field, finishing the rename on Enter or on the field losing the focus and dropping it on Escape.
    public static void Wire(TextBox box, Action finish, Action drop)
    {
        box.SetValue(IsRenameFieldProperty, true);
        box.KeyDown += (_, e) => AnswerKey(e, finish, drop);
        box.LostFocus += (_, _) => finish();
    }

    // Whether the focus is in a rename field, which takes Escape itself rather than leaving it to close or dock the window.
    public static bool TakesEscape(IInputElement? focused)
    {
        return focused is TextBox box && box.GetValue(IsRenameFieldProperty);
    }

    // Finishes a rename on Enter and drops it on Escape, marking either handled.
    private static void AnswerKey(KeyEventArgs e, Action finish, Action drop)
    {
        if (e.Key == Key.Enter)
        {
            finish();
        }
        else if (e.Key == Key.Escape)
        {
            drop();
        }
        else
        {
            return;
        }

        e.Handled = true;
    }
}
