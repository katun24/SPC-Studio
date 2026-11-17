using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.VisualTree;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// Where a drop-down's or a menu's list opens off the control that opens it: against the control's left edge, or against its right edge where the list
// would run off the screen from the left one or where the drop-down always opens its list that way.
public static class ListPlacements
{
    // Places the list of every drop-down and of every menu of the menu bar below its control, present and future.
    public static void PlaceEveryDropDownList()
    {
        Control.LoadedEvent.AddClassHandler<Popup>((popup, _) => PlaceBelowWhereDropDown(popup));
    }

    // Hangs a list off the side of its control given, from the control's left edge where the whole list fits on the screen that way and from its right
    // edge otherwise, the list's frame standing flush with that edge.
    public static void PlaceAgainstEdge(CustomPopupPlacement placement, Control? list, PopupAnchor side)
    {
        if (list is null || FitsOnScreenFromLeftEdge(placement, list))
        {
            HangFrom(placement, side, PopupAnchor.Left, PopupGravity.Right);
            return;
        }

        PlaceAgainstRightEdge(placement, list, side);
    }

    // Places a popup below its control this way where it wears the drop-down placement, against the right edge alone where its drop-down asks for that.
    private static void PlaceBelowWhereDropDown(Popup popup)
    {
        if (!popup.Classes.Contains(ThemeClasses.DropDown))
        {
            return;
        }

        popup.Placement = PlacementMode.Custom;
        if (popup.TemplatedParent is StyledElement dropDown && dropDown.Classes.Contains(ThemeClasses.PanelLibraryList))
        {
            popup.CustomPopupPlacementCallback = placement => PlaceAgainstRightEdge(placement, popup.Child, PopupAnchor.Bottom);
            return;
        }

        popup.CustomPopupPlacementCallback = placement => PlaceAgainstEdge(placement, popup.Child, PopupAnchor.Bottom);
    }

    // Hangs a list off the side of its control given from the control's right edge, the list's frame standing flush with it.
    private static void PlaceAgainstRightEdge(CustomPopupPlacement placement, Control? list, PopupAnchor side)
    {
        HangFrom(placement, side, PopupAnchor.Right, PopupGravity.Left);
        if (list is null)
        {
            return;
        }

        placement.Offset = new Point(placement.Offset.X + ShadowRoomRightOf(list), placement.Offset.Y);
    }

    // Hangs a list off the side of its control given at the corner of the edge given, running away from the control and in the direction given.
    private static void HangFrom(CustomPopupPlacement placement, PopupAnchor side, PopupAnchor edge, PopupGravity alongSide)
    {
        placement.ConstraintAdjustment &= ~PopupPositionerConstraintAdjustment.FlipX;
        PopupGravity awayFromControl = side == PopupAnchor.Top ? PopupGravity.Top : PopupGravity.Bottom;
        placement.Anchor = side | edge;
        placement.Gravity = awayFromControl | alongSide;
    }

    // Whether the whole list, its shadow room included, ends on the screen when it starts at its control's left edge.
    private static bool FitsOnScreenFromLeftEdge(CustomPopupPlacement placement, Control list)
    {
        if (TopLevel.GetTopLevel(placement.Target) is not { } topLevel || topLevel.Screens is not { } screens)
        {
            return true;
        }

        double listLeft = placement.AnchorRectangle.Left + placement.Offset.X;
        PixelPoint listLeftOnScreen = topLevel.PointToScreen(new Point(listLeft, placement.AnchorRectangle.Top));
        PixelPoint listRightOnScreen = topLevel.PointToScreen(new Point(listLeft + WidthOf(placement, list), placement.AnchorRectangle.Top));
        return screens.ScreenFromPoint(listLeftOnScreen) is not { } screen || listRightOnScreen.X <= screen.WorkingArea.Right;
    }

    // The list's width, measured here on its first opening, when the popup has not measured it yet.
    private static double WidthOf(CustomPopupPlacement placement, Control list)
    {
        if (placement.PopupSize.Width > 0)
        {
            return placement.PopupSize.Width;
        }

        list.Measure(Size.Infinity);
        return list.DesiredSize.Width;
    }

    // How far the list reaches past its frame's right edge: the shadow room the frame and every layer around it keep there.
    private static double ShadowRoomRightOf(Control list)
    {
        double shadowRoom = 0;
        Layoutable? frame = list.GetSelfAndVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Classes.Contains(ThemeClasses.PopupChrome));
        for (Layoutable? layer = frame; layer is not null; layer = layer.GetVisualParent() as Layoutable)
        {
            shadowRoom += layer.Margin.Right;
            if (layer == list)
            {
                return shadowRoom;
            }
        }

        return shadowRoom;
    }
}
