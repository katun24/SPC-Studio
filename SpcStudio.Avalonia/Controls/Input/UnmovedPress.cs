namespace SpcStudio.Controls;

// A plain press on something that can be dragged, which picks that one thing out of the selection where it is let go before the cursor has moved.
public sealed class UnmovedPress<T>
{
    private T? _pressedItem;
    private bool _isUnmoved;

    // Remembers what a plain press went down on.
    public void Begin(T pressedItem)
    {
        _pressedItem = pressedItem;
        _isUnmoved = true;
    }

    // Marks the cursor as having moved, answering whether this was the first movement of a press that had not moved yet.
    public bool TakeFirstMovement()
    {
        if (!_isUnmoved)
        {
            return false;
        }

        _isUnmoved = false;
        return true;
    }

    // Ends the press, handing back what it went down on where the cursor never moved.
    public bool TryEnd(out T pressedItem)
    {
        pressedItem = _pressedItem!;
        bool wasUnmoved = _isUnmoved;
        _isUnmoved = false;
        _pressedItem = default;
        return wasUnmoved;
    }
}
