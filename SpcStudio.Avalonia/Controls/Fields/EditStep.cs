using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SpcStudio.Controls;

// What an undo step is, for the whole app: a gesture, which every pointer press in a window starts afresh, and the key an edit is recorded under is
// that gesture paired with the control the edit came from; everything one press makes while it is being handled being one step, and a claim the
// press makes and the first edit it goes on to make being one step.
public static class EditStep
{
    private static object _currentGesture = new();
    private static object? _pressBeingHandled;

    // The source every claim records under: a press turning an inert control live, which the first edit the same press goes on to make joins.
    public static readonly object ClaimSource = new();

    // Starts a new gesture on every pointer press in every window, and marks every press in a window or a popup for as long as it is being handled.
    public static void StartGestureOnEveryPress()
    {
        InputElement.PointerPressedEvent.AddClassHandler<Window>((_, _) => _currentGesture = new object(), RoutingStrategies.Tunnel);
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, _) => _pressBeingHandled = new object(), RoutingStrategies.Tunnel);
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, _) => _pressBeingHandled = null, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // The key an edit is recorded under - the gesture, the control and the press being handled - or null for an edit that is no control's made outside
    // a press, which never merges.
    public static object? UndoKeyFor(object? source)
    {
        if (source is null && _pressBeingHandled is null)
        {
            return null;
        }

        return new UndoKey(_currentGesture, source, _pressBeingHandled);
    }
}
