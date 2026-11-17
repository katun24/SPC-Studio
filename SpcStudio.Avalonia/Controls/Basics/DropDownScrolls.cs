using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SpcStudio.Controls;

// A wheel notch anywhere over an open drop-down's list scrolls the list, the air around and between its rows included.
public static class DropDownScrolls
{
    // Hands a notch that lands on an open list outside the part that scrolls to the list's own scrolling, for every drop-down there is or will be.
    public static void ScrollListUnderEveryNotch()
    {
        InputElement.PointerWheelChangedEvent.AddClassHandler<ComboBox>(OnWheel, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // Scrolls an open drop-down's list by a notch that landed on the list but outside the part that scrolls.
    private static void OnWheel(ComboBox dropDown, PointerWheelEventArgs e)
    {
        if (!dropDown.IsDropDownOpen || e.Source is not Visual source || ListViewerAround(source) is not { Presenter: { } presenter } viewer)
        {
            return;
        }

        if (source == presenter || presenter.IsVisualAncestorOf(source))
        {
            return;
        }

        ScrollPans.PassNotchTo(viewer, e, e.KeyModifiers);
    }

    // The scroll viewer of the open list a visual stands in, or null where the visual is not in a list.
    private static ScrollViewer? ListViewerAround(Visual source)
    {
        if (source.GetVisualRoot() is not PopupRoot listPopup)
        {
            return null;
        }

        return listPopup.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }
}
