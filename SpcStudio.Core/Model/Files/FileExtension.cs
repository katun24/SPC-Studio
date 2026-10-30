namespace SpcStudio.Model;

// The one test of a file's extension, which every kind of file the app reads takes whatever the case it is written in.
public static class FileExtension
{
    // Whether a path ends in an extension, whatever its case.
    public static bool Matches(string path, string extension)
    {
        return Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase);
    }
}
