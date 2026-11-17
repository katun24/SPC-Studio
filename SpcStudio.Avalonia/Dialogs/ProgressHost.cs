using Avalonia.Controls;

namespace SpcStudio;

// Where a long job shows how far it has got, and the window its boxes stand over: behind a progress box of its own over a window, or as the item a
// batch's progress box already up is at.
public abstract class ProgressHost
{
    // Builds a host whose boxes stand over a window.
    private ProgressHost(Window boxOwner)
    {
        BoxOwner = boxOwner;
    }

    // The window every box a job under this host shows stands over.
    public Window BoxOwner { get; }

    // A host that runs every job behind a progress box of its own over a window, which its other boxes stand over too.
    public static ProgressHost OwnBox(Window owner)
    {
        return new OwnBoxHost(owner);
    }

    // A host that runs every job as the item a batch's progress box is at, reporting the share of the item done, stopping with the batch and showing its
    // other boxes over the batch's box.
    public static ProgressHost BatchItem(Window batchBox, Action<double> reportItemFraction, CancellationToken batchStop)
    {
        return new BatchItemHost(batchBox, reportItemFraction, batchStop);
    }

    // Runs a job off the interface thread, a box of its own saying a title and a line where it has one, and answers what it came to, every failure in its own words.
    public abstract Task<ProgressDialog.JobRun<T>> Run<T>(ProgressTitle title, string line, Func<Action<double>, CancellationToken, T> work);

    // The words a job's failure is shown in.
    private static string FailureMessageOf(Exception exception)
    {
        return exception.Message;
    }

    // Runs every job behind a progress box of its own over a window.
    private sealed class OwnBoxHost(Window owner) : ProgressHost(owner)
    {
        // Runs a job behind a box of its own saying a title and a line.
        public override Task<ProgressDialog.JobRun<T>> Run<T>(ProgressTitle title, string line, Func<Action<double>, CancellationToken, T> work)
        {
            return ProgressDialog.RunUnderOneLine(BoxOwner, title, line, work, FailureMessageOf);
        }
    }

    // Runs every job as the item a batch's progress box is at.
    private sealed class BatchItemHost(Window batchBox, Action<double> reportItemFraction, CancellationToken batchStop) : ProgressHost(batchBox)
    {
        // Runs a job under the batch's box, which keeps its own title and line.
        public override Task<ProgressDialog.JobRun<T>> Run<T>(ProgressTitle title, string line, Func<Action<double>, CancellationToken, T> work)
        {
            return ProgressDialog.Outcome(() => Task.Run(() => work(reportItemFraction, batchStop)), FailureMessageOf, batchStop);
        }
    }
}
