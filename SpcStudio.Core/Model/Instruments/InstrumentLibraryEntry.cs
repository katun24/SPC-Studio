namespace SpcStudio.Model;

// An entry of the instruments folder's tree: an instrument file, or a folder.
public sealed record InstrumentLibraryEntry(string Path, bool IsFolder);
