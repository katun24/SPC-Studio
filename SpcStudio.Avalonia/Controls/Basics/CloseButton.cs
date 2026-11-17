using Avalonia.Layout;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The Close button at the foot of a box whose settings apply as they are chosen, standing at the box's right and being its default.
public sealed class CloseButton : ThemedButton
{
    private const string CloseText = "Close";

    // Gives the button its text, its look and its place.
    public CloseButton()
    {
        Content = CloseText;
        IsDefault = true;
        HorizontalAlignment = HorizontalAlignment.Right;
        Classes.Add(ThemeClasses.DialogButton);
        Classes.Add(ThemeClasses.Emphasized);
    }
}
