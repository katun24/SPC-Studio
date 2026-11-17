using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SpcStudio.Controls;

namespace SpcStudio;

// The keys more than one window answers the same way.
public static class WindowKeys
{
    // Whether a key press asks to undo.
    public static bool IsUndo(KeyEventArgs e)
    {
        return Shortcuts.IsPressed(e, Shortcuts.Undo);
    }

    // Whether a key press asks to redo, on either of redo's shortcuts.
    public static bool IsRedo(KeyEventArgs e)
    {
        return Shortcuts.IsPressed(e, Shortcuts.Redo, Shortcuts.SecondRedo);
    }

    // Runs an undo on undo's keys and a redo on redo's, given the element that has the focus, marking the key handled and answering true where it was either.
    public static bool StepHistory(KeyEventArgs e, IInputElement? focused, Action undo, Action redo)
    {
        if (IsUndo(e))
        {
            UndoFrom(focused, undo);
        }
        else if (IsRedo(e))
        {
            redo();
        }
        else
        {
            return false;
        }

        e.Handled = true;
        return true;
    }

    // Undoes from wherever the focus is, first taking it out of an untouched field a press gave it to, and in a rename that has been typed into only
    // dropping the rename.
    private static void UndoFrom(IInputElement? focused, Action undo)
    {
        var focusManager = TopLevel.GetTopLevel(focused as Visual)?.FocusManager;
        bool isFieldUntouched = PressFocusedField.IsUntouched(focused);
        if (RenameField.TryDrop(focused) && !isFieldUntouched)
        {
            return;
        }

        if (isFieldUntouched)
        {
            focusManager?.ClearFocus();
        }

        undo();
    }

    // Whether a key press is the key a window plays with, whatever modifiers are held, where it does not land in a field that takes it as a character.
    public static bool IsPlayKey(KeyEventArgs e, IInputElement? focused)
    {
        return Shortcuts.IsKeyOf(e, Shortcuts.Play) && !FieldInput.TakesSpace(focused);
    }

    // Whether a key is one that deletes what is selected: Delete or Backspace.
    public static bool IsDeleteKey(Key key)
    {
        return key is Key.Delete or Key.Back;
    }

    // Whether a key press is Escape with no modifier held, which dismisses every window but the main one.
    public static bool IsBareEscape(KeyEventArgs e)
    {
        return e.KeyModifiers == KeyModifiers.None && e.Key == Key.Escape;
    }

    // Whether the focus is on text that answers select all and copy itself: a field, or a value that can be selected.
    public static bool IsOnSelectableText(IInputElement? focused)
    {
        return focused is TextBox or SelectableTextBlock;
    }
}
