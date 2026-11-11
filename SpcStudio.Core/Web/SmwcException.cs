namespace SpcStudio.Web;

// Why a song could not be had from SMWCentral, each made with the message that says why as a whole sentence.
public sealed class SmwcException : Exception
{
    private SmwcException(string reason, Exception? cause = null) : base(reason, cause)
    {
    }

    // A number SMWCentral has no song under.
    public static SmwcException NoSong(int songNumber)
    {
        return new SmwcException($"SMWCentral has no song with number {songNumber}.");
    }

    // A download that failed on the way.
    public static SmwcException DownloadFailed(Exception cause)
    {
        return new SmwcException("The song could not be downloaded from SMWCentral.", cause);
    }
}
