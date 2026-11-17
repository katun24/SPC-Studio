using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SpcStudio.Controls;

// What makes a text field a rename field: Enter, the field losing the focus or a menu bar's menu opening finishes the rename and Escape drops it, the old
// name staying - and a rename field is the one text field that keeps Escape from closing its window.
public static class RenameField
{
    private const string DropActionPropertyName = "DropAction";
    private const string FinishActionPropertyName = "FinishAction";

    // What drops a rename field's rename, which wiring it hangs on the field and which only a rename field has.
    private static readonly AttachedProperty<Action?> DropActionProperty = AvaloniaProperty.RegisterAttached<TextBox, Action?>(DropActionPropertyName, typeof(RenameField));

    // What finishes a rename field's rename, hung on the field beside the drop.
    private static readonly AttachedProperty<Action?> FinishActionProperty = AvaloniaProperty.RegisterAttached<TextBox, Action?>(FinishActionPropertyName, typeof(RenameField));

    // Makes a text field a rename field, finishing the rename on Enter or on the field losing the focus and dropping it on Escape.
    public static void Wire(TextBox box, Action finish, Action drop)
    {
        box.SetValue(DropActionProperty, drop);
        box.SetValue(FinishActionProperty, finish);
        FieldCommit.OnEnterOrLeave(box, finish);
        box.KeyDown += (_, e) => DropOnEscape(e, drop);
    }

    // Finishes the rename of a rename field that has the focus whenever a menu of a window's menu bar opens, in every window.
    public static void FinishWhenMenuBarOpens()
    {
        MenuItem.SubmenuOpenedEvent.AddClassHandler<MenuItem>((item, _) => FinishIfMenuBarMenu(item));
    }

    // Whether the focus is in a rename field, which takes Escape itself rather than leaving it to close or dock the window.
    public static bool TakesEscape(IInputElement? focused)
    {
        return ActionOf(focused, DropActionProperty) is not null;
    }

    // Drops the rename of a rename field that has the focus, answering whether the focus was in one.
    public static bool TryDrop(IInputElement? focused)
    {
        if (ActionOf(focused, DropActionProperty) is not { } drop)
        {
            return false;
        }

        drop();
        return true;
    }

    // Finishes the rename of a rename field that has the focus where a menu that opened stands on a menu bar.
    private static void FinishIfMenuBarMenu(MenuItem item)
    {
        if (item.Parent is not Menu)
        {
            return;
        }

        ActionOf(TopLevel.GetTopLevel(item)?.FocusManager?.GetFocusedElement(), FinishActionProperty)?.Invoke();
    }

    // The action of the field that has the focus held under a property, or nothing where the focus is in no rename field.
    private static Action? ActionOf(IInputElement? focused, AttachedProperty<Action?> property)
    {
        return focused is TextBox box ? box.GetValue(property) : null;
    }

    // Drops a rename on Escape, marking the key handled.
    private static void DropOnEscape(KeyEventArgs e, Action drop)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        drop();
        e.Handled = true;
    }
}
