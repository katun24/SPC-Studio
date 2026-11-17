using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SpcStudio;

// The box that chooses what an extraction writes or an addition adds: a message, a checked box for every instrument or sample the source holds, and a
// confirming button, which can be pressed only while any box is checked.
public partial class ExtractionDialog : DialogWindow
{
    // Builds the box.
    public ExtractionDialog()
    {
        InitializeComponent();
    }

    // The list's check boxes, in the order of the names they were built for.
    private IReadOnlyList<CheckBox> ItemBoxes
    {
        get
        {
            return [.. ItemList.Children.OfType<CheckBox>()];
        }
    }

    // Whether any of the list's boxes is checked.
    private bool IsAnyBoxChecked
    {
        get
        {
            return ItemBoxes.Any(box => box.IsChecked == true);
        }
    }

    // Shows the box under a title over a window, a message above a checked box for each name and the confirming button's words, and answers the
    // positions of the names left checked, or null where it was cancelled.
    public static Task<IReadOnlyList<int>?> Show(Window owner, string title, string message, IReadOnlyList<string> names, string confirmText)
    {
        var dialog = new ExtractionDialog();
        dialog.ShowTitle(title, null);
        dialog.MessageText.Text = message;
        dialog.ButtonRow.ConfirmText = confirmText;
        foreach (string name in names)
        {
            var box = new CheckBox { Content = new TextBlock { Text = name }, IsChecked = true };
            box.IsCheckedChanged += (_, _) => dialog.ShowConfirmEnabled();
            dialog.ItemList.Children.Add(box);
        }

        return dialog.ShowDialog<IReadOnlyList<int>?>(owner);
    }

    // Enables the confirming button exactly while any box is checked.
    private void ShowConfirmEnabled()
    {
        ButtonRow.IsConfirmEnabled = IsAnyBoxChecked;
    }

    // Closes the box answering the positions of the checked boxes, where any is checked.
    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (!IsAnyBoxChecked)
        {
            return;
        }

        Close(ItemBoxes.Select((box, position) => (box, position)).Where(numberedBox => numberedBox.box.IsChecked == true).Select(numberedBox => numberedBox.position).ToList());
    }
}
