namespace SpcStudio.Controls;

// How Shift steers a surface a click places on - the roll and a lane of units alike: a running drag switched between moving and resizing, and with no
// drag running the placement preview's own resize begun and ended, the latch a Shift+wheel sets holding the resize off until Shift is let go.
public sealed class ShiftResize<T>
{
    private readonly PlacementPreview<T> _preview;
    private readonly IShiftResizeSurface _surface;

    // Takes the preview Shift resizes and the surface whose drag it switches.
    public ShiftResize(PlacementPreview<T> preview, IShiftResizeSurface surface)
    {
        _preview = preview;
        _surface = surface;
    }

    // Whether the running drag resizes what it holds rather than moving it.
    public bool IsDragResizing { get; set; }

    // Tells a running drag, or the preview under the cursor, whether Shift is down.
    public void SetModifier(bool isShiftHeld)
    {
        if (!_preview.ShouldActOnShift(isShiftHeld))
        {
            return;
        }

        if (_surface.IsDragRunning)
        {
            SwitchDragMode(isShiftHeld);
            return;
        }

        if (isShiftHeld == _preview.IsResizing)
        {
            return;
        }

        if (!isShiftHeld)
        {
            _preview.EndResize();
            _surface.ShowPreviewAtCursor();
            return;
        }

        if (_preview.TryBeginResize())
        {
            _surface.RebasePreviewResize();
        }
    }

    // Turns the resize off until Shift is let go.
    public void Suppress()
    {
        SetModifier(false);
        _preview.SuppressResizeUntilShiftReleased();
    }

    // Switches a running drag between moving and resizing, keeping what the last mode did.
    private void SwitchDragMode(bool isResizing)
    {
        if (IsDragResizing == isResizing)
        {
            return;
        }

        _surface.RebaseDrag();
        IsDragResizing = isResizing;
        _surface.FollowCursorWithDrag();
    }
}
