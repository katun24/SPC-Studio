using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SpcStudio.Controls;

// When a tooltip shows, for the whole app: only once the pointer has rested unmoved on its control for a moment, hiding the instant the pointer moves, presses or leaves the window.
public static class StillPointerTooltips
{
    private const int RestMilliseconds = 500;

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

        _tipCarrierUnderPointer = TipCarrierAt(e.Source as Visual);
        if (_tipCarrierUnderPointer is not null)
        {
            PointerRestTimer.Start();
        }
    }

    // The nearest control at or above a visual that carries a tooltip, or null.
    private static Control? TipCarrierAt(Visual? visual)
    {
        for (Visual? current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Control control && ToolTip.GetTip(control) is not null)
            {
                return control;
            }
        }

        return null;
    }

    // Opens the resting control's tooltip once the rest is over.
    private static void ShowAfterRest()
    {
        PointerRestTimer.Stop();
        if (_tipCarrierUnderPointer is null || _tipCarrierUnderPointer.GetVisualRoot() is null)
        {
            return;
        }

        ToolTip.SetIsOpen(_tipCarrierUnderPointer, true);
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
