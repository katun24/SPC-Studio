namespace SpcStudio.Import;

// A file that is not an SPC dump, each made with the message that says why.
public sealed class SpcReadException : Exception
{
    private SpcReadException(string message) : base(message)
    {
    }

    // A file too short to hold an SPC dump or without an SPC dump's signature.
    public static SpcReadException NotValidSpc()
    {
        return new SpcReadException("The selected file is not a valid SPC file.");
    }
}
