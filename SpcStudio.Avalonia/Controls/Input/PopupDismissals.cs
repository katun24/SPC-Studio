using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;

namespace SpcStudio.Controls;

// The one rule every popup that a press outside it closes obeys: the press goes on to the control under it, and a popup that press opened closes
// on a press outside it in turn.
public static class PopupDismissals
{
    private static readonly HashSet<Popup> OpenPopups = new();

    // Lets a press that closes a popup go on to the control under it, and keeps every popup still open closing on a press outside it.
    public static void PassPressesThroughClosingPopups(Application app)
    {
        app.Styles.Add(new Style(x => x.OfType<Popup>()) { Setters = { new Setter(Popup.OverlayDismissEventPassThroughProperty, true) } });
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup, bool>(OnOpenChanged);
    }

    // Keeps count of the popups open, and shows the press-catching layer again for those still open when one closes.
    private static void OnOpenChanged(Popup popup, AvaloniaPropertyChangedEventArgs<bool> e)
    {
        if (e.NewValue.GetValueOrDefault())
        {
            OpenPopups.Add(popup);
            return;
        }

        OpenPopups.Remove(popup);
        foreach (Popup stillOpen in OpenPopups)
        {
            ShowDismissLayerOf(stillOpen);
        }
    }

    // Shows the layer that catches a press outside a popup, where the popup closes on one and its layer is hidden.
    private static void ShowDismissLayerOf(Popup popup)
    {
        if (!popup.IsLightDismissEnabled || LightDismissOverlayLayer.GetLightDismissOverlayLayer(popup.PlacementTarget ?? popup) is not { IsVisible: false } layer)
        {
            return;
        }

        layer.IsVisible = true;
        layer.InputPassThroughElement = popup.OverlayInputPassThroughElement;
    }
}
