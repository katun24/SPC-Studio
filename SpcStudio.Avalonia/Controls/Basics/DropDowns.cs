using Avalonia;
using Avalonia.Controls;

namespace SpcStudio.Controls;

// The one rule every drop-down in the app obeys: a list with nothing in it is not shown.
public static class DropDowns
{
    // Whether a drop-down may be shown if it has items, which is true unless a control says otherwise.
    public static readonly AttachedProperty<bool> CanShowProperty = AvaloniaProperty.RegisterAttached<ComboBox, bool>("CanShow", typeof(DropDowns), defaultValue: true);

    // Writes whether a drop-down may be shown.
    public static void SetCanShow(ComboBox dropDown, bool canShow)
    {
        dropDown.SetValue(CanShowProperty, canShow);
    }

    // Reads whether a drop-down may be shown.
    public static bool GetCanShow(ComboBox dropDown)
    {
        return dropDown.GetValue(CanShowProperty);
    }

    // Hangs the rule on every drop-down there is or will be, checked when its item count changes, when it may or may not be shown, and when it loads.
    public static void HideWhileEmpty()
    {
        ItemsControl.ItemCountProperty.Changed.AddClassHandler<ComboBox, int>((dropDown, _) => ShowOnlyWithItems(dropDown));
        CanShowProperty.Changed.AddClassHandler<ComboBox, bool>((dropDown, _) => ShowOnlyWithItems(dropDown));
        Control.LoadedEvent.AddClassHandler<ComboBox>((dropDown, _) => ShowOnlyWithItems(dropDown));
    }

    // Shows a drop-down where it has items and may be shown, and hides it otherwise.
    private static void ShowOnlyWithItems(ComboBox dropDown)
    {
        dropDown.IsVisible = dropDown.ItemCount > 0 && GetCanShow(dropDown);
    }
}
