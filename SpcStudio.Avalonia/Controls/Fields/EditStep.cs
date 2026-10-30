using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SpcStudio.Controls;

// What an undo step is, for the whole app: a gesture, which every pointer press in a window starts afresh, and the key an edit is recorded under is
// that gesture paired with the control the edit came from; a claim the press makes and the first edit it goes on to make being one step.
public static class EditStep
{
    private static object _currentGesture = new();

    // The source every claim records under: a press turning an inert control live, which the first edit the same press goes on to make joins.
    public static readonly object ClaimSource = new();

    // Starts a new gesture on every pointer press in every window.
    public static void StartGestureOnEveryPress()
    {
        InputElement.PointerPressedEvent.AddClassHandler<Window>((_, _) => _currentGesture = new object(), RoutingStrategies.Tunnel);
    }

    // The key an edit from a control is recorded under - the gesture and the control - or null for an edit that is no control's, which never merges.
    public static object? UndoKeyFor(object? source)
    {
        return source is null ? null : new UndoKey(_currentGesture, source);
    }
}
