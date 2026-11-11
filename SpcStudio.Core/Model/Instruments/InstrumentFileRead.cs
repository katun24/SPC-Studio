namespace SpcStudio.Model;

// One instrument file as read at a write time: the instrument it holds, or the words saying why it cannot be read.
public sealed record InstrumentFileRead(DateTime? WriteTime, InstrumentFile? File, string Refusal)
{
    private const string NoRefusal = "";

    // Reads one instrument file, a file that cannot be read answering why rather than throwing.
    public static InstrumentFileRead Of(string path)
    {
        var writeTime = WriteTimeOf(path);

        try
        {
            return new InstrumentFileRead(writeTime, InstrumentFile.Load(path), NoRefusal);
        }
        catch (Exception ex)
        {
            return new InstrumentFileRead(writeTime, null, ex.Message);
        }
    }

    // When a file was last written, or null where the system cannot say.
    public static DateTime? WriteTimeOf(string path)
    {
        try
        {
            return System.IO.File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
