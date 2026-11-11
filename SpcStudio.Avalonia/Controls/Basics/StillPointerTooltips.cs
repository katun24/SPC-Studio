using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SpcStudio.Controls;

// When a tooltip shows, for the whole app: only once the pointer has rested unmoved on its control for a moment, hiding the instant the pointer moves, presses or leaves the window.
public static class StillPointerTooltips
{
    private const int RestMilliseconds = 500;
    private const double PointerAnchorWidth = 1;

    private static readonly DispatcherTimer PointerRestTimer = new() { Interval = TimeSpan.FromMilliseconds(RestMilliseconds) };
    private static Control? _tipCarrierUnderPointer;
    private static PixelPoint? _pointerOnScreenAtLastMove;

    // Turns off Avalonia's own tooltip timing and hangs this rule on every window and popup.
    public static void ShowOnlyAfterStillPointer()
    {
        Control.LoadedEvent.AddClassHandler<TopLevel>((topLevel, _) => ToolTip.SetServiceEnabled(topLevel, false));
        PointerRestTimer.Tick += (_, _) => ShowAfterRest();
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, _) => HideAndStopWaiting(), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerExitedEvent.AddClassHandler<TopLevel>((_, _) => HideAndStopWaiting());
    }

    // Hides any tooltip on a real movement and starts the rest again over whatever control carrying a tooltip is under the pointer.
    private static void OnPointerMoved(TopLevel topLevel, PointerEventArgs e)
    {
        PixelPoint pointerOnScreen = topLevel.PointToScreen(e.GetPosition(topLevel));
        if (pointerOnScreen == _pointerOnScreenAtLastMove)
        {
            return;
        }

        _pointerOnScreenAtLastMove = pointerOnScreen;
        HideAndStopWaiting();
        if (PointerButtons.IsAnyHeld(e.GetCurrentPoint(topLevel).Properties))
        {
            return;
        }

        TopLevel topLevelUnderPointer = (e.Source as Visual)?.GetVisualRoot() as TopLevel ?? topLevel;
        _tipCarrierUnderPointer = TipCarrierAt(topLevelUnderPointer.InputHitTest(e.GetPosition(topLevelUnderPointer), enabledElementsOnly: false) as Visual);
        if (_tipCarrierUnderPointer is not null)
        {
            PointerRestTimer.Start();
        }
    }

    // The nearest control at or above a visual that carries a tooltip it may show, or null.
    private static Control? TipCarrierAt(Visual? visual)
    {
        for (Visual? current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Control control && ToolTip.GetTip(control) is not null && CanShowTip(control))
            {
                return control;
            }
        }

        return null;
    }

    // Whether a control carrying a tooltip may show it: any enabled control, and a button disabled too.
    private static bool CanShowTip(Control control)
    {
        return control.IsEffectivelyEnabled || control is Button;
    }

    // Opens the resting control's tooltip once the rest is over.
    private static void ShowAfterRest()
    {
        PointerRestTimer.Stop();
        if (_tipCarrierUnderPointer is null || _tipCarrierUnderPointer.GetVisualRoot() is null)
        {
            return;
        }

        ToolTip.SetPlacement(_tipCarrierUnderPointer, PlacementMode.Custom);
        ToolTip.SetCustomPopupPlacementCallback(_tipCarrierUnderPointer, PlaceClearOfPointer);
        ToolTip.SetIsOpen(_tipCarrierUnderPointer, true);
    }

    // Places a tooltip below and right of the pointer by its offset, as Avalonia's pointer placement does, but the same distance above the pointer
    // where it does not fit below, so it never opens over the pointer.
    private static void PlaceClearOfPointer(CustomPopupPlacement placement)
    {
        if (TopLevel.GetTopLevel(placement.Target) is not { } topLevel || _pointerOnScreenAtLastMove is not { } pointerOnScreen)
        {
            return;
        }

        Point pointer = topLevel.PointToClient(pointerOnScreen);
        double gapFromPointer = placement.Offset.Y;
        placement.AnchorRectangle = new Rect(pointer.X + placement.Offset.X, pointer.Y - gapFromPointer, PointerAnchorWidth, 2 * gapFromPointer);
        placement.Anchor = PopupAnchor.BottomLeft;
        placement.Gravity = PopupGravity.BottomRight;
        placement.Offset = default;
    }

    // Closes the tooltip showing, if any, and forgets the rest under way.
    private static void HideAndStopWaiting()
    {
        PointerRestTimer.Stop();
        if (_tipCarrierUnderPointer is null)
        {
            return;
        }

        ToolTip.SetIsOpen(_tipCarrierUnderPointer, false);
        _tipCarrierUnderPointer = null;
    }
}
