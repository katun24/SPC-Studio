using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace SpcStudio;

// Welcomes the user at launch where no AddmusicK install the app can run is set and asks for one, OK being enabled only while one has been picked or typed.
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
        dialog.ShowBoxTitle(WindowTitles.Plain.Welcome);
        dialog.ShowWelcomeMessage();
        dialog.AmkFolderRow.ShownPath = amkDirectory;
        PathPicks.Wire(dialog, dialog.AmkFolderRow, PathPicks.AmkFolder, directory => dialog.ButtonRow.IsConfirmEnabled = directory is not null);
        return dialog.ShowDialog<string?>(owner);
    }

    // Writes the welcome, its line with the link opening the manual, and the request for an install, each paragraph parted from the next by an empty line.
    private void ShowWelcomeMessage()
    {
        var manualLink = new HyperlinkButton { Content = QuestionsAndNotices.ManualLinkText, UseLayoutRounding = false };
        manualLink.Click += OnManualClick;
        Inline[][] paragraphs =
        [
            [new Run(QuestionsAndNotices.Welcome)],
            [new Run(QuestionsAndNotices.ManualLinkLead), new InlineUIContainer(manualLink) { BaselineAlignment = BaselineAlignment.TextBottom }, new Run(QuestionsAndNotices.ManualLinkEnd)],
            [new Run(QuestionsAndNotices.ChooseAmkFolder), new LineBreak(), new Run(QuestionsAndNotices.FeaturesDisabledWithoutAmk)],
        ];
        var message = new InlineCollection();
        foreach (Inline[] paragraph in paragraphs)
        {
            if (message.Count > 0)
            {
                message.Add(new LineBreak());
                message.Add(new LineBreak());
            }

            message.AddRange(paragraph);
        }

        MessageText.Inlines = message;
    }

    // Opens the manual.
    private async void OnManualClick(object? sender, RoutedEventArgs e)
    {
        await SystemOpen.OpenManual(this);
    }

    // Closes the box answering the install chosen.
    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close(AmkFolderRow.ShownPath);
    }
}
