using Avalonia.Input;

namespace SpcStudio;

// The key combinations the app answers with a modifier or names in a menu or a tooltip, each written as it is shown, and the tests of a key press
// against them.
public static class Shortcuts
{
    public const string NewSong = "Ctrl+N";
    public const string OpenSong = "Ctrl+O";
    public const string SaveSong = "Ctrl+S";
    public const string SaveSongAs = "Ctrl+Shift+S";
    public const string Undo = "Ctrl+Z";
    public const string Redo = "Ctrl+Y";
    public const string SecondRedo = "Ctrl+Shift+Z";
    public const string SelectAll = "Ctrl+A";
    public const string SelectAllChannels = "Ctrl+Alt+A";
    public const string Cut = "Ctrl+X";
    public const string Copy = "Ctrl+C";
    public const string Paste = "Ctrl+V";
    public const string Play = "Space";
    public const string PlayLooping = "Shift+Space";
    public const string PlaySelection = "Ctrl+Space";
    public const string PlaySelectionLooping = "Ctrl+Shift+Space";
    public const string PlayInstrument = "Alt+Space";
    public const string AddSong = OpenSong;
    public const string SaveSongList = SaveSong;

    // Whether a key press is any of the shortcuts, with exactly its modifiers held.
    public static bool IsPressed(KeyEventArgs e, params string[] shortcuts)
    {
        return shortcuts.Any(shortcut => KeyGesture.Parse(shortcut).Matches(e));
    }

    // Runs what the first of a window's shortcuts a key press is runs, marking the press handled, and answers whether it was any of them.
    public static bool TryRun(KeyEventArgs e, IEnumerable<(string Shortcut, Action Run)> shortcuts)
    {
        if (shortcuts.FirstOrDefault(shortcut => IsPressed(e, shortcut.Shortcut)).Run is not { } run)
        {
            return false;
        }

        run();
        e.Handled = true;
        return true;
    }

    // Whether a key press or release is a shortcut's key, whatever modifiers are held with it.
    public static bool IsKeyOf(KeyEventArgs e, string shortcut)
    {
        return e.Key == KeyGesture.Parse(shortcut).Key;
    }
}
