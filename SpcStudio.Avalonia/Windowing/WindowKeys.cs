using Avalonia.Controls;
using Avalonia.Input;
using SpcStudio.Controls;

namespace SpcStudio;

// The keys more than one window answers the same way.
public static class WindowKeys
{
    // Whether a key press asks to undo: Ctrl+Z.
    public static bool IsUndo(KeyEventArgs e)
    {
        return e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Z;
    }

    // Whether a key press asks to redo: Ctrl+Y or Ctrl+Shift+Z.
    public static bool IsRedo(KeyEventArgs e)
    {
        return e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Y || e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.Z;
    }

    // Runs an undo on undo's keys and a redo on redo's, marking the key handled and answering true where it was either.
    public static bool StepHistory(KeyEventArgs e, Action undo, Action redo)
    {
        if (IsUndo(e))
        {
            undo();
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

    // Whether a key press is the space bar a window plays with: one that does not land in a field where a space is a character.
    public static bool IsPlayKey(KeyEventArgs e, IInputElement? focused)
    {
        return e.Key == Key.Space && !FieldInput.TakesSpace(focused);
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

    // Whether the focus is on text that answers Ctrl+A and Ctrl+C itself: a field, or a value that can be selected.
    public static bool IsOnSelectableText(IInputElement? focused)
    {
        return focused is TextBox or SelectableTextBlock;
    }
}
