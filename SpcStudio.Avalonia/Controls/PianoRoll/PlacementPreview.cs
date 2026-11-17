namespace SpcStudio.Controls;

// What a click on empty space would put down, drawn under the cursor before the click - on the roll and on a lane of units alike: the members, the
// Shift resize that stretches them, and the latch a Shift+wheel sets.
public sealed class PlacementPreview<T>
{
    private readonly List<T> _members = [];
    private bool _isResizeSuppressed;
    private bool _hasUniformLength;

    // What a click would place, in the order the surface built it, the anchor first.
    public IReadOnlyList<T> Members
    {
        get
        {
            return _members;
        }
    }

    public int Count
    {
        get
        {
            return _members.Count;
        }
    }

    // Whether Shift is resizing the preview, with no button down.
    public bool IsResizing { get; private set; }

    // The members a Shift resize has left showing, which a click lays down as they are, or null where no resize is showing any.
    public List<T>? ResizedMembers()
    {
        return IsResizing && _members.Count > 0 ? [.. _members] : null;
    }

    // The one length every member is laid down at - the length given, where a Shift resize has given them one - or null where each keeps its own.
    public int? UniformLength(int placedLengthTicks)
    {
        return _hasUniformLength ? placedLengthTicks : null;
    }

    // Puts these members under the cursor, and answers whether that changed anything.
    public bool Show(IReadOnlyList<T> next)
    {
        if (_members.SequenceEqual(next))
        {
            return false;
        }

        _members.Clear();
        _members.AddRange(next);
        return true;
    }

    // Takes the preview away and ends a resize, and answers whether anything was drawn.
    public bool Hide()
    {
        IsResizing = false;
        return Show([]);
    }

    // Starts a Shift resize where there is something to resize, and answers whether one started.
    public bool TryBeginResize()
    {
        if (_members.Count == 0)
        {
            return false;
        }

        IsResizing = true;
        return true;
    }

    // Ends a Shift resize, leaving the length it reached for the next click.
    public void EndResize()
    {
        IsResizing = false;
    }

    // Puts the members a resize has carried to under the cursor, every one of them at one length, and answers whether that changed anything.
    public bool ShowResized(IReadOnlyList<T> next)
    {
        if (!Show(next))
        {
            return false;
        }

        _hasUniformLength = true;
        return true;
    }

    // Forgets the one length a resize gave the members, the selection they came from having changed.
    public void ForgetUniformLength()
    {
        _hasUniformLength = false;
    }

    // Whether a Shift state a key or a pointer event reported is to be acted on, a release clearing the latch and a hold ignored while it is set.
    public bool ShouldActOnShift(bool isShiftHeld)
    {
        if (!isShiftHeld)
        {
            _isResizeSuppressed = false;
            return true;
        }

        return !_isResizeSuppressed;
    }

    // Turns the resize off for as long as Shift is held.
    public void SuppressResizeUntilShiftReleased()
    {
        _isResizeSuppressed = true;
    }
}
