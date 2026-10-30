using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SpcStudio;

// The app's message box: a message over a row of answer buttons under a title whose kind gives it its icon, shown over a window and awaited for the answer.
public partial class MessageDialog : DialogWindow
{
    public const string ParagraphBreak = "\n\n";

    private bool _isHeldOpen;

    // Builds the box.
    public MessageDialog()
    {
        InitializeComponent();
    }

    // Shows a message with the buttons asked for over a window, and answers what the user pressed when the box is closed.
    public static Task<MessageAnswer> Show(Window owner, string message, BoxTitle title, MessageButtons buttons = MessageButtons.Ok)
    {
        return Built(message, title, buttons).ShowDialog<MessageAnswer>(owner);
    }

    // Shows a message with no buttons over a window while a job runs off the interface thread, the box closing only once the job has ended.
    public static async Task ShowWhile(Window owner, string message, BoxTitle title, Action job)
    {
        var dialog = Built(message, title, MessageButtons.None);
        dialog._isHeldOpen = true;
        var shown = dialog.ShowDialog<MessageAnswer>(owner);
        try
        {
            await Task.Run(job);
        }
        finally
        {
            dialog._isHeldOpen = false;
            dialog.Close();
            await shown;
        }
    }

    // Asks over a window whether to save a document's changes before an action, saving where the answer is yes, and answers whether the action may go on.
    public static async Task<bool> AskToSaveBefore(Window owner, string documentName, string action, Func<Task<bool>> save)
    {
        var answer = await Show(owner, QuestionsAndNotices.SaveChangesBefore(documentName, action), WindowTitles.Notices.SaveChanges, MessageButtons.YesNoCancel);
        return answer switch
        {
            MessageAnswer.Yes => await save(),
            MessageAnswer.No => true,
            _ => false,
        };
    }

    // Runs a step, saying in a box over a window under a failure title what went wrong where it throws, answering whether it ran.
    public static async Task<bool> TryOrSayFailure(Window owner, Action step, FailureTitle failureTitle)
    {
        try
        {
            step();
            return true;
        }
        catch (Exception ex)
        {
            await Show(owner, ex.Message, failureTitle);
            return false;
        }
    }

    // Refuses every close while the box is held open for a job.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_isHeldOpen)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    // A box with a title, its icon, a message and the buttons asked for, not yet shown.
    private static MessageDialog Built(string message, BoxTitle title, MessageButtons buttons)
    {
        var dialog = new MessageDialog();
        dialog.ShowBoxTitle(title);
        dialog.MessageText.Text = message;

        dialog.ButtonRow.IsVisible = buttons != MessageButtons.None;
        bool isQuestion = buttons is MessageButtons.YesNo or MessageButtons.YesNoCancel;
        dialog.YesButton.IsVisible = isQuestion;
        dialog.NoButton.IsVisible = isQuestion;
        dialog.CancelButton.IsVisible = buttons == MessageButtons.YesNoCancel;
        dialog.OkButton.IsVisible = buttons == MessageButtons.Ok;
        return dialog;
    }

    // Closes the box answering OK.
    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close(MessageAnswer.Ok);
    }

    // Closes the box answering Yes.
    private void OnYesClick(object? sender, RoutedEventArgs e)
    {
        Close(MessageAnswer.Yes);
    }

    // Closes the box answering No.
    private void OnNoClick(object? sender, RoutedEventArgs e)
    {
        Close(MessageAnswer.No);
    }

    // Closes the box answering Cancel.
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(MessageAnswer.Cancel);
    }
}
