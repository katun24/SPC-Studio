namespace SpcStudio;

// One file an export writes into a folder: its name there, and how to write it to a path.
internal sealed record ExportedFile(string FileName, Action<string> WriteTo);
