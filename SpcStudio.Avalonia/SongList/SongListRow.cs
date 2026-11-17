using Avalonia.Controls;

namespace SpcStudio;

// One row of the song list window as it stands on screen: its number, the controls showing it, and the file it was last shown playing.
public sealed class SongListRow(int number, Border view, TextBlock songText, Button clearButton, Button loadButton, Button editButton)
{
    public int Number { get; } = number;
    public Border View { get; } = view;
    public TextBlock SongText { get; } = songText;
    public Button ClearButton { get; } = clearButton;
    public Button LoadButton { get; } = loadButton;
    public Button EditButton { get; } = editButton;

    // The file the row plays, named relative to the install's music folder, or null where it plays none.
    public string? ListName { get; set; }
}
