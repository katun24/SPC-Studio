using Avalonia.Input;

namespace SpcStudio.Controls;

// The Home and End keys' pan of a scrolled view to the start or the end of its content.
public static class ViewEdges
{
    // The one of two values that stands for an edge: the first for the start, the second for the end.
    public static T ValueAt<T>(ViewEdge edge, T atStart, T atEnd)
    {
        return edge == ViewEdge.Start ? atStart : atEnd;
    }

    // Pans a view across to an edge of its content, leaving its vertical offset where it is.
    public static void PanTo(IScrolledView view, ViewEdge edge)
    {
        view.Offset = view.Offset.WithX(ValueAt(edge, 0, view.FurthestOffset.X));
    }

    // Pans a view to the edge Home or End stands for, held bare or with modifiers that name an action, which is run at that edge first - answering
    // whether the key was taken.
    public static bool TryPanToEdgeOfKey(IScrolledView view, KeyEventArgs e, Func<KeyModifiers, Action<ViewEdge>?> edgeActionOf)
    {
        if (OfKey(e.Key) is not ViewEdge edge)
        {
            return false;
        }

        var edgeAction = edgeActionOf(e.KeyModifiers);
        if (edgeAction is null && e.KeyModifiers != KeyModifiers.None)
        {
            return false;
        }

        edgeAction?.Invoke(edge);
        PanTo(view, edge);
        return true;
    }

    // The edge Home or End pans to, or null for any other key.
    private static ViewEdge? OfKey(Key key)
    {
        return key switch
        {
            Key.Home => ViewEdge.Start,
            Key.End => ViewEdge.End,
            _ => null,
        };
    }
}
