using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SpcStudio;

// A modal box in front of a job long enough to look like a freeze: what the job is doing, a bar, and a Cancel button that asks the job to stop and leaves the box up until it has.
public partial class ProgressDialog : DialogWindow
{
    private const int DoneColumn = 0;
    private const int RemainingColumn = 1;

    private readonly CancellationTokenSource _cancellation = new();
    private bool _isFinished;

    // Runs whatever the job reports on the UI thread the box is made on.
    private readonly IProgress<Action> _onUiThread = new Progress<Action>(show => show());

    // Builds the box.
    public ProgressDialog()
    {
        InitializeComponent();
    }

    // Runs a job off the UI thread with one of these boxes in front of it, showing each stage the job reports, and answers what it produced, the words
    // for a failure it has words for, and whether it was stopped; a failure it has no words for is thrown once the box is down.
    public static Task<JobRun<T>> Run<T>(Window owner, string title, string openingLine, Func<JobProgress, CancellationToken, T> work,
        Func<Exception, string?>? failureMessageOf = null, CancellationToken otherStop = default)
    {
        return RunBehindBox(owner, title, openingLine,
            (dialog, cancel) => work((stage, doneCount, totalCount, itemFraction) => dialog._onUiThread.Report(() => dialog.ShowProgress(stage, doneCount, totalCount, itemFraction)), cancel),
            failureMessageOf, otherStop);
    }

    // Runs a job as Run does behind a box that says one line throughout, the bar showing the share of the whole job it reports done.
    public static Task<JobRun<T>> RunUnderOneLine<T>(Window owner, string title, string line, Func<Action<double>, CancellationToken, T> work,
        Func<Exception, string?>? failureMessageOf = null, CancellationToken otherStop = default)
    {
        return RunBehindBox(owner, title, line, (dialog, cancel) => work(doneFraction => dialog._onUiThread.Report(() => dialog.FillBar(doneFraction)), cancel), failureMessageOf,
            otherStop);
    }

    // How far through a count a job is, an item part done counting for its part; nothing where there is nothing to count.
    public static double DoneFraction(long doneCount, long totalCount, double itemFraction = 0)
    {
        if (totalCount <= 0)
        {
            return 0;
        }

        return (doneCount + itemFraction) / totalCount;
    }

    // Runs a job off the UI thread with a box opened on a line in front of it, handing the job the box to report to.
    private static async Task<JobRun<T>> RunBehindBox<T>(Window owner, string title, string openingLine, Func<ProgressDialog, CancellationToken, T> work,
        Func<Exception, string?>? failureMessageOf, CancellationToken otherStop = default)
    {
        var dialog = new ProgressDialog { Title = title };
        dialog.ShowStage(openingLine);

        var shown = dialog.ShowDialog(owner);
        T? value = default;
        string? failureMessage = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(dialog._cancellation.Token, otherStop);

        try
        {
            value = await Task.Run(() => work(dialog, stop.Token));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            failureMessage = failureMessageOf?.Invoke(ex);
            if (failureMessage is null)
            {
                throw;
            }
        }
        finally
        {
            dialog.Finish();
            await shown;
        }

        return new JobRun<T>(value, failureMessage, stop.IsCancellationRequested);
    }

    // Asks the job to stop when the frame's own close is pressed before the job has finished, keeping the box up.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isFinished)
        {
            e.Cancel = true;
            RequestCancel();
        }

        base.OnClosing(e);
    }

    // Shows what the job reported: a counted stage's count, or the sentence of a stage with nothing to count.
    private void ShowProgress(string stage, int doneCount, int totalCount, double itemFraction)
    {
        if (totalCount <= 0)
        {
            ShowStage(stage);
            return;
        }

        ShowCount(stage, doneCount, totalCount, itemFraction);
    }

    // Shows a stage with nothing to count: its sentence and an empty bar.
    private void ShowStage(string sentence)
    {
        StageText.Text = sentence;
        FillBar(0);
    }

    // Shows how far a counted stage has got, in its own noun, with the bar that follows from it.
    private void ShowCount(string noun, int doneCount, int totalCount, double itemFraction)
    {
        StageText.Text = $"{doneCount} of {totalCount} {noun}";
        FillBar(DoneFraction(doneCount, totalCount, itemFraction));
    }

    // Fills the bar to a fraction, from nothing to all of it.
    private void FillBar(double doneFraction)
    {
        double shownFraction = Math.Clamp(doneFraction, 0, 1);
        BarColumns.ColumnDefinitions[DoneColumn].Width = new GridLength(shownFraction, GridUnitType.Star);
        BarColumns.ColumnDefinitions[RemainingColumn].Width = new GridLength(shownFraction >= 1 ? 0 : 1 - shownFraction, GridUnitType.Star);
    }

    // Closes the box, the job having stopped.
    private void Finish()
    {
        _isFinished = true;
        Close();
    }

    // Asks the job to stop and disables the button, the box staying up until the job has stopped.
    private void RequestCancel()
    {
        CancelButton.IsEnabled = false;
        _cancellation.Cancel();
    }

    // Asks the job to stop when Cancel is pressed.
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        RequestCancel();
    }

    // What a job run behind one of these boxes came to: what it produced, the words to show where it failed, and whether it was stopped.
    public readonly record struct JobRun<T>(T? Value, string? FailureMessage, bool IsCancelled);
}
