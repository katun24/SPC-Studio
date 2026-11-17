using System.IO;

namespace SpcStudio.Model;

// Reading files where one that cannot be read counts as one that is not there.
public static class ReadableFiles
{
    // What a read answers, or null where a file it reads cannot be read.
    public static T? OrNull<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // A file's bytes, or null where it is not there or cannot be read.
    public static byte[]? BytesOrNull(string path)
    {
        return OrNull(() => File.Exists(path) ? File.ReadAllBytes(path) : null);
    }
}
