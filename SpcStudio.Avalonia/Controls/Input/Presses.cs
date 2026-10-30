using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace SpcStudio.Controls;

// The one rule every control that is pressed obeys: it acts on the press rather than on the release, and only for the left button - a button of
// every kind, a drop-down's face and its items, and a menu's items.
public static class Presses
{
    private static IPointer? _lastPressedPointer;

    // Makes every pressable control there is or will be act on the press, lets a press that closes a popup go on to the control under it, and takes
    // the capture off a press whose window stops being the active one.
    public static void MakeEveryControlActOnPress(Application app)
    {
        app.Styles.Add(new Style(x => x.Is<Button>()) { Setters = { new Setter(Button.ClickModeProperty, ClickMode.Press) } });
        app.Styles.Add(new Style(x => x.OfType<Popup>()) { Setters = { new Setter(Popup.OverlayDismissEventPassThroughProperty, true) } });
        InputElement.PointerPressedEvent.AddClassHandler<InputElement>(OnPressed, RoutingStrategies.Tunnel);
        InputElement.PointerReleasedEvent.AddClassHandler<InputElement>(OnReleased, RoutingStrategies.Tunnel);
        WindowBase.IsActiveProperty.Changed.AddClassHandler<WindowBase>(OnActiveChanged);
    }

    // Takes the capture off the pointer of the last press where a window holding it, or a closed pop-up, stops being the active one.
    private static void OnActiveChanged(WindowBase window, AvaloniaPropertyChangedEventArgs e)
    {
        if (window.IsActive || _lastPressedPointer?.Captured is not Visual captured || ElementWindow.Of(captured) is { } capturingWindow && capturingWindow != window)
        {
            return;
        }

        _lastPressedPointer.Capture(null);
    }

    // Acts on a press at the element it landed on, where that is part of a menu item, a drop-down's item outside a button on it or a drop-down's face.
    private static void OnPressed(InputElement element, PointerPressedEventArgs e)
    {
        _lastPressedPointer = e.Pointer;

        if (e.Handled || element != e.Source)
        {
            return;
        }

        bool isLeftButton = e.GetCurrentPoint(element).Properties.IsLeftButtonPressed;

        if (element.FindAncestorOfType<MenuItem>(includeSelf: true) is { } menuItem)
        {
            if (isLeftButton && !menuItem.HasSubMenu && menuItem.IsEffectivelyEnabled)
            {
                FocusFirstFocusableFrom(element, e);
                ClickMenuItem(menuItem);
                e.Handled = true;
            }

            return;
        }

        if (DropDownItemOf(element) is var (dropDown, container))
        {
            if (element.FindAncestorOfType<Button>(includeSelf: true) is not null)
            {
                return;
            }

            if (isLeftButton)
            {
                PickDropDownItem(dropDown, container);
            }

            e.Handled = true;
            return;
        }

        if (element.FindAncestorOfType<ComboBox>(includeSelf: true) is { } face)
        {
            if (isLeftButton)
            {
                FocusFirstFocusableFrom(element, e);
                face.SetCurrentValue(ComboBox.IsDropDownOpenProperty, !face.IsDropDownOpen);
            }

            e.Handled = true;
        }
    }

    // Takes the release off a drop-down's item, which is where the stock control would pick it.
    private static void OnReleased(InputElement element, PointerReleasedEventArgs e)
    {
        if (element == e.Source && DropDownItemOf(element) is not null)
        {
            e.Handled = true;
        }
    }

    // The drop-down item an element is part of and the drop-down it belongs to, or null for an element in no item.
    private static (ComboBox DropDown, ComboBoxItem Container)? DropDownItemOf(Visual element)
    {
        if (element.FindAncestorOfType<ComboBoxItem>(includeSelf: true) is { } container && ItemsControl.ItemsControlFromItemContainer(container) is ComboBox dropDown)
        {
            return (dropDown, container);
        }

        return null;
    }

    // Selects a drop-down's item and closes its list.
    private static void PickDropDownItem(ComboBox dropDown, ComboBoxItem container)
    {
        int index = dropDown.IndexFromContainer(container);
        if (index >= 0)
        {
            dropDown.SelectedIndex = index;
        }

        dropDown.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
    }

    // Focuses the first element up from the one pressed that can hold the focus.
    private static void FocusFirstFocusableFrom(Visual element, PointerPressedEventArgs e)
    {
        for (Visual? ancestor = element; ancestor is not null; ancestor = ancestor.GetVisualParent())
        {
            if (ancestor is InputElement { Focusable: true, IsEffectivelyEnabled: true } target)
            {
                target.Focus(NavigationMethod.Pointer, e.KeyModifiers);
                return;
            }
        }
    }

    // Clicks a menu item as its menu would on the release: a check or a radio item flips, the click is raised, and the menu closes unless the item keeps it open.
    private static void ClickMenuItem(MenuItem menuItem)
    {
        if (menuItem.ToggleType == MenuItemToggleType.CheckBox)
        {
            menuItem.SetCurrentValue(MenuItem.IsCheckedProperty, !menuItem.IsChecked);
        }
        else if (menuItem.ToggleType == MenuItemToggleType.Radio && !menuItem.IsChecked)
        {
            menuItem.SetCurrentValue(MenuItem.IsCheckedProperty, true);
        }

        menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        if (!menuItem.StaysOpenOnClick)
        {
            menuItem.FindLogicalAncestorOfType<MenuBase>()?.Close();
        }
    }
}
