using Avalonia.Controls;

namespace SpcStudio;

// The guard a window or a control holds over a write of its own, so the write is not read back as the user's edit.
public static class FlagGuard
{
    // Sets a flag for as long as an action runs.
    public static void RunWithFlagSet(ref bool flag, Action action)
    {
        flag = true;
        try
        {
            action();
        }
        finally
        {
            flag = false;
        }
    }

    // Marks a control as the one an edit is coming from for as long as an action runs, so the redraw inside it leaves that control alone.
    public static void RunWithControlMarked(ref Control? markedControl, Control? control, Action action)
    {
        markedControl = control;
        try
        {
            action();
        }
        finally
        {
            markedControl = null;
        }
    }
}
