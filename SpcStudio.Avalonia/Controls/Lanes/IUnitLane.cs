namespace SpcStudio.Controls;

// What a lane of units offers the window, with the unit's own type left out: the selection, the clipboard and the keys that act on them.
public interface IUnitLane
{
    // Raised where this lane has taken the unit selection, for every other lane to drop its own and for the window to drop the roll's.
    event Action? SelectionTaken;

    // Whether any unit is selected, which is what Delete and copy act on.
    bool HasSelection { get; }

    // Whether this lane is the one a paste should read.
    bool HasClipboard { get; }

    // Takes the selected units off the song.
    bool DeleteSelected();

    // Copies the selected units and takes the clipboard off whatever held it.
    bool CopySelected();

    // Copies and then deletes the selected units, as one edit.
    bool CutSelected();

    // Puts the clipboard down at a tick.
    bool Paste(int anchorTick);

    // Drops the unit selection, which selecting notes does.
    void ClearUnitSelection();

    // Tells a drag in progress, or the preview under the cursor, whether Shift is down.
    void SetResizeModifier(bool isShiftHeld);

    // Turns the resize off until Shift is let go.
    void SuppressResize();

    // Re-runs the cursor over content a scroll or a zoom has moved under it.
    void ContentMoved();
}
