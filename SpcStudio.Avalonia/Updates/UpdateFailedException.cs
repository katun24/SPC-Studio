namespace SpcStudio;

// Why an update could not be installed where the install has its own words for it - a folder the app cannot write to, or a download that failed.
public sealed class UpdateFailedException : Exception
{
    // A failure with the words that say what went wrong, and what the system threw where it threw.
    private UpdateFailedException(string message, Exception? cause = null) : base(message, cause)
    {
    }

    // The app's folder could not be written to.
    public static UpdateFailedException FolderNotWritable(string folder)
    {
        return new UpdateFailedException(QuestionsAndNotices.CannotWriteTo(folder));
    }

    // The download did not arrive whole.
    public static UpdateFailedException DownloadFailed(Exception cause)
    {
        return new UpdateFailedException(QuestionsAndNotices.UpdateDownloadFailed, cause);
    }
}
