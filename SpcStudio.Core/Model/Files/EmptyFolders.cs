using System.IO;

namespace SpcStudio.Model;

// Deleting a folder only while it is empty.
public static class EmptyFolders
{
    // Deletes the folder where nothing is in it, passing over one that holds anything, is gone or cannot be deleted.
    public static void DeleteWhereEmpty(string path)
    {
        try
        {
            Directory.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
