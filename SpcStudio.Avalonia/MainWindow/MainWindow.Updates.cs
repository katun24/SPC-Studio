using System.Diagnostics;
using Avalonia.Interactivity;

namespace SpcStudio;

// The main window's part that checks for a newer release of the app, at launch and from the Help menu, and installs one the user accepts.
public partial class MainWindow
{
    // Checks for a newer release as the app starts where the preference asks for it, saying nothing where there is none or GitHub cannot be asked.
    private async Task CheckForUpdatesAtLaunch()
    {
        if (!_doesCheckForUpdatesAtLaunch || (await UpdateCheck.AskForNewerRelease()).Update is not { } update)
        {
            return;
        }

        await OfferUpdate(update);
    }

    // Checks for a newer release from the Help menu, saying so where there is none and where GitHub cannot be asked.
    private async void OnCheckForUpdatesClick(object? sender, RoutedEventArgs e)
    {
        var answer = await UpdateCheck.AskForNewerRelease();
        if (answer.HasFailed)
        {
            await MessageDialog.Show(this, QuestionsAndNotices.UpdateCheckFailed, WindowTitles.Failures.FailedToCheckForUpdates);
            return;
        }

        if (answer.Update is not { } update)
        {
            await MessageDialog.Show(this, QuestionsAndNotices.LatestVersionInUse, WindowTitles.Notices.NoUpdateAvailable);
            return;
        }

        await OfferUpdate(update);
    }

    // Asks whether to install a newer release, and where the answer is yes downloads and installs it behind a progress box and starts the app again from it, saying what went wrong where it fails.
    private async Task OfferUpdate(AvailableUpdate update)
    {
        string version = update.Version.ToString();
        if (await MessageDialog.Show(this, QuestionsAndNotices.UpdateAvailable(version), WindowTitles.Notices.UpdateAvailable, MessageButtons.YesNo) != MessageAnswer.Yes)
        {
            return;
        }

        string? programPath = Environment.ProcessPath;
        var run = await ProgressDialog.RunUnderOneLine(this, WindowTitles.DownloadingUpdate, QuestionsAndNotices.DownloadingUpdate(version), (reportDone, cancel) =>
        {
            UpdateInstall.Install(update, AppPaths.AppFolder, reportDone, cancel);
            return true;
        }, UpdateFailureMessage);

        if (run.FailureMessage is { } failureMessage)
        {
            await MessageDialog.Show(this, failureMessage, WindowTitles.Failures.FailedToUpdate);
            return;
        }

        if (!run.Value)
        {
            return;
        }

        await RestartFrom(programPath);
    }

    // Closes the app, asking about unsaved changes as a close does, and starts it again from the program's path, which the update has put the new
    // program at; an answer that keeps the app open leaves the update to the next launch.
    private async Task RestartFrom(string? programPath)
    {
        if (!await ConfirmSongListAndSongBefore(QuestionsAndNotices.UpdateAction))
        {
            return;
        }

        CloseWithoutAsking();
        if (programPath is null)
        {
            return;
        }

        Process.Start(programPath);
    }

    // The words for a failed update: the install's own where it has them, and otherwise the app's followed by the system's.
    private static string UpdateFailureMessage(Exception failure)
    {
        return failure is UpdateFailedException ? failure.Message : QuestionsAndNotices.UpdateInstallFailed(failure.Message);
    }
}
