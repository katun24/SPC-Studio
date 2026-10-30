using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace SpcStudio.Controls;

// Which window an element stands in, a pop-up's element standing in the window the pop-up belongs to.
public static class ElementWindow
{
    // The window an element stands in, or null for an element of a closed window or a closed pop-up.
    public static TopLevel? Of(Visual element)
    {
        return element.GetVisualRoot() switch
        {
            PopupRoot popup => popup.ParentTopLevel,
            TopLevel window => window,
            _ => null,
        };
    }
}
