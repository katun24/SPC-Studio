namespace SpcStudio.Model;

// Every instrument file read while the app runs, each kept with the write time it was read at, so a file is read again only once it has changed on
// disk; safe to use from any thread.
public sealed class InstrumentFileStore
{
    private readonly object _readsLock = new();
    private readonly Dictionary<string, InstrumentFileRead> _readsByPath = new(StringComparer.OrdinalIgnoreCase);

    // What was read of a file, where it has not changed on disk since; null where it has not been read at its current write time.
    public InstrumentFileRead? CurrentRead(string path)
    {
        if (InstrumentFileRead.WriteTimeOf(path) is not { } writeTime)
        {
            return null;
        }

        lock (_readsLock)
        {
            return _readsByPath.TryGetValue(path, out var read) && read.WriteTime == writeTime ? read : null;
        }
    }

    // What a file holds: what was read of it where it has not changed since, and otherwise what reading it now gives, which is kept.
    public InstrumentFileRead Read(string path)
    {
        if (CurrentRead(path) is { } current)
        {
            return current;
        }

        var read = InstrumentFileRead.Of(path);
        lock (_readsLock)
        {
            _readsByPath[path] = read;
        }

        return read;
    }

    // Forgets every file but the ones named.
    public void KeepOnly(IReadOnlySet<string> paths)
    {
        lock (_readsLock)
        {
            foreach (string path in _readsByPath.Keys.Where(path => !paths.Contains(path)).ToList())
            {
                _readsByPath.Remove(path);
            }
        }
    }
}
