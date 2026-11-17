namespace SpcStudio;

// What an export of one file is of: its save picker's title, file types, extension, id and starting folder, and the title its failure is reported under.
internal sealed record FileExportKind(string PickerTitle, string Filter, string Extension, string PickerId, string StartFolder, FailureTitle FailureTitle);
