using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SpcStudio;

// Asks at launch for the AddmusicK install where none the app can run is set, OK being enabled only while one has been picked or typed.
public partial class AmkFolderPromptWindow : DialogWindow
{
    // Builds the box.
    public AmkFolderPromptWindow()
    {
        InitializeComponent();
    }

    // Shows the box over a window showing the install set now, and answers the folder chosen, or null where it was cancelled.
    public static Task<string?> Show(Window owner, string? amkDirectory)
    {
        var dialog = new AmkFolderPromptWindow();
        dialog.ShowBoxTitle(WindowTitles.Notices.ChooseAmkFolder);
        dialog.MessageText.Text = QuestionsAndNotices.ChooseAmkFolder;
        dialog.AmkFolderRow.ShownPath = amkDirectory;
        PathPicks.Wire(dialog, dialog.AmkFolderRow, PathPicks.AmkFolder, directory => dialog.ButtonRow.IsConfirmEnabled = directory is not null);
        return dialog.ShowDialog<string?>(owner);
    }

    // Closes the box answering the install chosen.
    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close(AmkFolderRow.ShownPath);
    }
}
