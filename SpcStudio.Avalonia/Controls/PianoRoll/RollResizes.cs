using SpcStudio.Model;

namespace SpcStudio.Controls;

// How the roll and the lanes tell the window that an edit changed how long the roll is.
public static class RollResizes
{
    // Raises an event where the song's roll is no longer the length it had before an edit.
    public static void RaiseIfChanged(Song song, int rollTicksBefore, Action? rollResized)
    {
        if (song.RollLengthTicks != rollTicksBefore)
        {
            rollResized?.Invoke();
        }
    }
}
