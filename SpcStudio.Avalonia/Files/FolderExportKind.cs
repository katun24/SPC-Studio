namespace SpcStudio;

// What an export of a whole list into a folder is of: the noun its report counts in, its folder picker's title, id and starting folder, the title of its
// overwrite questions and the title of its report.
internal sealed record FolderExportKind(string Noun, string PickerTitle, string PickerId, string StartFolder, NoticeTitle QuestionTitle, SuccessTitle ReportTitle);
