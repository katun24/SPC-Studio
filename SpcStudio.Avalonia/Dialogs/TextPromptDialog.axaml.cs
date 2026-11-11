using Avalonia.Controls;
using Avalonia.Interactivity;
using SpcStudio.Controls;

namespace SpcStudio;

// A box that asks for one line of text: a message, a field and a confirming button that can be pressed only while the field holds a text the prompt can use.
public partial class TextPromptDialog : DialogWindow
{
    private TextPrompt? _prompt;

    // Builds the box.
    public TextPromptDialog()
    {
        InitializeComponent();
    }

    // Whether the field holds a text the prompt can use.
    private bool IsTextUsable
    {
        get
        {
            return _prompt is not null && _prompt.IsUsable(FieldBox.Text);
        }
    }

    // Shows the box asking with a prompt over a window, the field holding a text to start from and the caret in it, and answers the text typed, or null where it was cancelled.
    public static Task<string?> Show(Window owner, TextPrompt prompt, string? startingText)
    {
        var dialog = new TextPromptDialog { _prompt = prompt };
        dialog.ShowTitle(prompt.Title, null);
        dialog.MessageText.Text = prompt.Message;
        FieldInput.SetAccepts(dialog.FieldBox, prompt.Accepts);
        if (prompt.ConfirmText is not null)
        {
            dialog.ButtonRow.ConfirmText = prompt.ConfirmText;
        }

        dialog.FieldBox.Text = startingText;
        dialog.ShowConfirmEnabled();
        dialog.Opened += (_, _) => FieldInput.FocusField(dialog.FieldBox);
        return dialog.ShowDialog<string?>(owner);
    }

    // Enables the confirming button while the field holds a text the prompt can use.
    private void OnFieldChanged(object? sender, TextChangedEventArgs e)
    {
        ShowConfirmEnabled();
    }

    // Enables the confirming button exactly while the field holds a text the prompt can use.
    private void ShowConfirmEnabled()
    {
        ButtonRow.IsConfirmEnabled = IsTextUsable;
    }

    // Closes the box answering the text typed, where the prompt can use it.
    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (!IsTextUsable)
        {
            return;
        }

        Close(FieldBox.Text);
    }
}
