using Avalonia.Controls;
using Avalonia.Interactivity;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// Asks for the folder a song's samples are exported into, for an export that needs one and a song that has none.
public partial class SamplePathDialog : DialogWindow
{
    // Builds the box.
    public SamplePathDialog()
    {
        InitializeComponent();
    }

    // Shows the box over a window holding what the song has now, with the caret in the field, and answers the folder name, or null where it was cancelled.
    public static Task<string?> Show(Window owner, string currentPath)
    {
        var dialog = new SamplePathDialog();
        dialog.MessageText.Text = QuestionsAndNotices.NoSamplePath;
        dialog.PathBox.Text = currentPath;
        dialog.ShowOkEnabled();
        dialog.Opened += (_, _) => FieldInput.FocusField(dialog.PathBox);
        return dialog.ShowDialog<string?>(owner);
    }

    // Enables OK while the field holds a name the export can use.
    private void OnPathChanged(object? sender, TextChangedEventArgs e)
    {
        ShowOkEnabled();
    }

    // Enables OK exactly while the field holds a name the export can use.
    private void ShowOkEnabled()
    {
        ButtonRow.IsConfirmEnabled = DiskNameRules.IsValid(PathBox.Text);
    }

    // Closes the box answering the folder name, where the field holds one the export can use.
    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (!DiskNameRules.IsValid(PathBox.Text))
        {
            return;
        }

        Close(DiskNameRules.Normalize(PathBox.Text));
    }
}
