using Avalonia.Controls;
using Avalonia.Input;

namespace SpcStudio.Controls;

// How a text field whose text is taken whole, rather than as it is typed, hands it over: on Enter or on the field losing the focus.
public static class FieldCommit
{
    // Runs an action whenever Enter is pressed in a field, marking the key handled, and whenever the field loses the focus.
    public static void OnEnterOrLeave(TextBox box, Action commit)
    {
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            commit();
            e.Handled = true;
        };
        box.LostFocus += (_, _) => commit();
    }
}
