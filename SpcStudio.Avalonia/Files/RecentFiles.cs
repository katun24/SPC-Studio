namespace SpcStudio;

// The files the File menu's Recent Files lists, newest first and each once: every song opened or saved, MML or MIDI imported and SPC transcribed.
public sealed class RecentFiles
{
    private const int MaxListedFiles = 15;

    private readonly List<RecentFile> _files = [];

    // Raised whenever a file is put on the list or taken off it.
    public event Action? ListChanged;

    public IReadOnlyList<RecentFile> FilesNewestFirst
    {
        get
        {
            return _files;
        }
    }

    // Puts a remembered list in place of this one, as many of its files as the list holds.
    public void Restore(IEnumerable<RecentFile> rememberedFiles)
    {
        _files.Clear();
        _files.AddRange(rememberedFiles.Take(MaxListedFiles));
        ListChanged?.Invoke();
    }

    // Puts a file at the top, taking it from wherever it stood lower, and drops the oldest where the list holds too many.
    public void Remember(string path, SongSourceKind kind)
    {
        RemoveListed(path);
        _files.Insert(0, new RecentFile(path, kind));
        if (_files.Count > MaxListedFiles)
        {
            _files.RemoveRange(MaxListedFiles, _files.Count - MaxListedFiles);
        }

        ListChanged?.Invoke();
    }

    // Takes a file off the list.
    public void Forget(string path)
    {
        RemoveListed(path);
        ListChanged?.Invoke();
    }

    // Removes a file where it is listed, its path matched whatever the case of its letters.
    private void RemoveListed(string path)
    {
        _files.RemoveAll(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase));
    }
}
