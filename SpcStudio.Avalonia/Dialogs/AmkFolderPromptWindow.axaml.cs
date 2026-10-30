using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SpcStudio;

// Asks at launch for the AddmusicK install where none the app can run is set, OK being enabled only once one has been picked.
public partial class AmkFolderPromptWindow : SettingsWidthDialogWindow
{
    // Builds the box.
    public AmkFolderPromptWindow()
    {
        InitializeComponent();
    }

    // Shows the box over a window showing the install set now, and answers the folder picked, or null where it was cancelled.
    public static Task<string?> Show(Window owner, string? amkDirectory)
    {
        var dialog = new AmkFolderPromptWindow();
        dialog.ShowBoxTitle(WindowTitles.Notices.ChooseAmkFolder);
        dialog.MessageText.Text = QuestionsAndNotices.ChooseAmkFolder;
        dialog.AmkFolderRow.ShownPath = amkDirectory;
        return dialog.ShowDialog<string?>(owner);
    }

    // Picks the install, starting at the one shown, and enables OK where it can be run.
    private async void OnAmkBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (await AmkFolderPick.PickUsableInto(this, AmkFolderRow) is null)
        {
            return;
        }

        ButtonRow.IsConfirmEnabled = true;
    }

    // Closes the box answering the install picked.
    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close(AmkFolderRow.ShownPath);
    }
}
