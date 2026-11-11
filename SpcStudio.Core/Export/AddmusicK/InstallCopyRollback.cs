using System.IO;

namespace SpcStudio.Export;

// The files and folders a copy into the install has made and the files it has written over, so that a copy failing part way can take them back out
// and put the overwritten ones back.
internal sealed class InstallCopyRollback
{
    private readonly List<string> _createdFiles = [];
    private readonly List<string> _createdFolders = [];
    private readonly List<(string Path, byte[] Bytes)> _overwrittenFiles = [];

    // Readies a file the copy is about to write: creates the folders it needs and notes it as made, or keeps its bytes where a file is already there.
    public void NoteBeforeWriting(string path)
    {
        CreateFolders(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            _overwrittenFiles.Add((path, File.ReadAllBytes(path)));
            return;
        }

        _createdFiles.Add(path);
    }

    // Deletes the files the copy made, puts back the files it wrote over and then deletes its folders, the deepest first, going on past any that cannot be
    // deleted or put back.
    public void TakeBack()
    {
        foreach (string file in _createdFiles)
        {
            AmkRun.DeleteWorkingFile(file);
        }

        foreach (var (path, bytes) in _overwrittenFiles)
        {
            try
            {
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        for (int i = _createdFolders.Count - 1; i >= 0; i--)
        {
            try
            {
                Directory.Delete(_createdFolders[i]);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // Creates a folder and every missing one above it, noting each one made, the deepest last.
    private void CreateFolders(string folder)
    {
        var missingFolders = new Stack<string>();
        for (string? current = folder; current is not null && !Directory.Exists(current); current = Path.GetDirectoryName(current))
        {
            missingFolders.Push(current);
        }

        foreach (string missingFolder in missingFolders)
        {
            Directory.CreateDirectory(missingFolder);
            _createdFolders.Add(missingFolder);
        }
    }
}
