namespace SpcStudio;

// One file Recent Files lists: its full path and what it becomes the song by.
public sealed record RecentFile(string Path, SongSourceKind Kind);
