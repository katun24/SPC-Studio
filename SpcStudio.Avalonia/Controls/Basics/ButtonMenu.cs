using Avalonia.Controls;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A menu opened off a button, which can be put back against the button while it stays open, after the button has moved or the menu has changed size.
public sealed class ButtonMenu : MenuFlyout
{
    private const double PlacementNudge = 1;

    // Whether the menu opens above its button rather than below it.
    private bool IsOpeningUpward
    {
        get
        {
            return Placement is PlacementMode.Top or PlacementMode.TopEdgeAlignedLeft or PlacementMode.TopEdgeAlignedRight;
        }
    }

    // Places the open menu against its button again, where the button stands now and at the menu's own size now.
    public void PlaceAgain()
    {
        if (!IsOpen)
        {
            return;
        }

        double offset = Popup.VerticalOffset;
        Popup.VerticalOffset = offset + PlacementNudge;
        Popup.VerticalOffset = offset;
    }

    // Opens the menu off a control, which takes the pointer while the menu is open, so it lights up under the pointer and a press on it shuts the menu.
    protected override bool ShowAtCore(Control placementTarget, bool showAtPointer = false)
    {
        OverlayInputPassThroughElement = placementTarget;
        return base.ShowAtCore(placementTarget, showAtPointer);
    }

    // The menu's presenter, keeping no shadow room below its frame where the menu opens upward.
    protected override Control CreatePresenter()
    {
        var presenter = base.CreatePresenter();
        if (!IsOpeningUpward)
        {
            return presenter;
        }

        presenter.Classes.Add(ThemeClasses.OpensUpward);
        return presenter;
    }
}
