namespace SpcStudio.Export;

// One row of the song list: a song number and the file it plays, named relative to the install's music folder, or null where it plays none.
public readonly record struct SongListEntry(int Number, string? ListName);
