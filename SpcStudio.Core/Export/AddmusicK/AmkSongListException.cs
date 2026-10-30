namespace SpcStudio.Export;

// Why AddmusicK's song list could not be read or written, or a song could not be put into it, each made with the message that says which.
public sealed class AmkSongListException : Exception
{
    // A refusal with the message that says why.
    private AmkSongListException(string reason) : base(reason)
    {
    }

    // A list file that could not be read, with what the system said.
    public static AmkSongListException NotRead(string path, string systemMessage)
    {
        return new AmkSongListException($"{path} could not be read: {systemMessage}");
    }

    // A list file that could not be written, with what the system said.
    public static AmkSongListException NotWritten(string path, string systemMessage)
    {
        return new AmkSongListException($"{path} could not be written: {systemMessage}");
    }

    // A list whose first song comes before any "Globals:" label.
    public static AmkSongListException NoGlobalsLabel()
    {
        return new AmkSongListException($"{AmkSongList.FileName} has a song before its \"Globals:\" label.");
    }

    // A line that does not start with a song number in hex.
    public static AmkSongListException InvalidNumber(string line)
    {
        return new AmkSongListException($"{AmkSongList.FileName} has a line that does not start with a song number: {line}");
    }

    // A song number no song can have.
    public static AmkSongListException NumberOutOfRange(int number)
    {
        return new AmkSongListException(
            $"{AmkSongList.FileName} lists a song as {number:X2}, which no song can be. "
            + $"Song numbers run from {AmkSongList.FirstSongNumber:X2} to {AmkSongList.LastSongNumber:X2}.");
    }

    // A local song numbered at or below a global one.
    public static AmkSongListException LocalNotAboveGlobals(int number)
    {
        return new AmkSongListException(
            $"{AmkSongList.FileName} lists local song {number:X2} at or below a global song. Every local song has to come after every global one.");
    }

    // A sample a song names that is neither beside it nor in the install.
    public static AmkSongListException SampleMissing(string name, string besideSongPath, string samplesFolderPath)
    {
        return new AmkSongListException($"Sample \"{name}\" is not at {besideSongPath} or at {samplesFolderPath}, so the song was not added.");
    }

    // A file in the install holding something else under the name a copy would take.
    public static AmkSongListException FileInTheWay(string path)
    {
        return new AmkSongListException($"{path} is already there and holds something else. Nothing was copied.");
    }

    // A copy into the install that failed part way, with what the system said.
    public static AmkSongListException CopyFailed(string systemMessage)
    {
        return new AmkSongListException($"The song could not be copied into the AddmusicK folder: {systemMessage}. Nothing was copied.");
    }
}
