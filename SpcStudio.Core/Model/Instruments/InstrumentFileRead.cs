using SpcStudio.Audio;

namespace SpcStudio.Model;

// One instrument file, or one instrument of a song read as one, as read at a write time: the instrument it holds, or the words saying why it cannot be read.
public sealed record InstrumentFileRead(DateTime? WriteTime, InstrumentFile? File, string Refusal)
{
    private const string NoRefusal = "";

    // Reads one instrument file, a file that cannot be read answering why rather than throwing.
    public static InstrumentFileRead Of(string path)
    {
        return Attempt(WriteTimeOf(path), () => InstrumentFile.Load(path));
    }

    // Reads one instrument of a song as a file would hold it with the sample it plays, one whose sample is missing answering why rather than throwing.
    public static InstrumentFileRead OfInstrument(Instrument instrument, Sample? sample)
    {
        return Attempt(null, () => InstrumentFile.Of(instrument, sample));
    }

    // What a reading of an instrument file answers, at a write time: the file, or the words saying why it failed.
    private static InstrumentFileRead Attempt(DateTime? writeTime, Func<InstrumentFile> read)
    {
        try
        {
            return new InstrumentFileRead(writeTime, read(), NoRefusal);
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
