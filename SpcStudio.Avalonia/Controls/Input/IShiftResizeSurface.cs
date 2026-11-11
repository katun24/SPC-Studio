namespace SpcStudio.Controls;

// What a surface hands the Shift resize steering it: whether a drag is running, and the steps that measure the drag or the preview afresh and carry them
// to the cursor.
public interface IShiftResizeSurface
{
    // Whether a drag of what a press placed or took hold of is running, which Shift switches between moving and resizing.
    bool IsDragRunning { get; }

    // Makes what the drag holds as it now stands the baseline the next mode measures against, from where the cursor now is.
    void RebaseDrag();

    // Carries the drag to where the cursor now is, by its current mode.
    void FollowCursorWithDrag();

    // Puts the preview back under the cursor, at whatever length a resize left behind.
    void ShowPreviewAtCursor();

    // Makes the preview as it now stands the baseline its resize measures against, from where the cursor now is.
    void RebasePreviewResize();
}
