using Avalonia.Controls;

namespace SpcStudio;

// Writing files into a folder the user picks: a whole list a file at a time, the question asked before writing over a file already there, and the write itself.
internal static class FolderExport
{
    // Writes every file of a list into a folder the user picks, one at a time, and reports what landed and what did not.
    public static async Task WriteAll(Window owner, FolderExportKind kind, IReadOnlyList<ExportedFile> files)
    {
        string? folder = await FilePickers.PickFolder(owner, kind.PickerTitle, kind.PickerId, kind.StartFolder);
        if (folder is null)
        {
            return;
        }

        var failures = new List<string>();
        int writtenCount = 0;
        int skippedCount = 0;
        foreach (var file in files)
        {
            var answer = await AskToWrite(owner, Path.Combine(folder, file.FileName), QuestionsAndNotices.OverwriteFile(file.FileName), MessageButtons.YesNoCancel, kind.QuestionTitle);
            if (answer == MessageAnswer.Cancel)
            {
                break;
            }

            if (answer == MessageAnswer.No)
            {
                skippedCount++;
                continue;
            }

            if (WriteInto(folder, file) is { } failure)
            {
                failures.Add(failure);
            }
            else
            {
                writtenCount++;
            }
        }

        string report = WriteAllReport(kind.Noun, folder, writtenCount, skippedCount);
        if (failures.Count == 0)
        {
            await MessageDialog.Show(owner, report, kind.ReportTitle);
            return;
        }

        await MessageDialog.Show(owner, string.Join(MessageDialog.ParagraphBreak, failures) + MessageDialog.ParagraphBreak + report, WindowTitles.Failures.FailedToExport(failures.Count, kind.Noun));
    }

    // Whether a file may be written: yes where nothing is in the way, and otherwise the answer to a question asking whether to overwrite it.
    public static async Task<MessageAnswer> AskToWrite(Window owner, string path, string overwriteQuestion, MessageButtons buttons, NoticeTitle title)
    {
        if (!File.Exists(path))
        {
            return MessageAnswer.Yes;
        }

        return await MessageDialog.Show(owner, overwriteQuestion, title, buttons);
    }

    // Writes one file into a folder under its own name, and answers null where that worked or the failure to report where it did not.
    public static string? WriteInto(string folder, ExportedFile file)
    {
        try
        {
            file.WriteTo(Path.Combine(folder, file.FileName));
            return null;
        }
        catch (Exception ex)
        {
            return $"{file.FileName} could not be written - {ex.Message}";
        }
    }

    // The sentence that ends an export of a whole list: how many files were written and where, and how many were skipped.
    private static string WriteAllReport(string noun, string folder, int writtenCount, int skippedCount)
    {
        // How many files were written and where, or that none were.
        string report = writtenCount == 0 ? $"No {CountedNoun.NounFor(writtenCount, noun)} were written." : $"{CountedNoun.Of(writtenCount, noun)} {WasOrWere(writtenCount)} written to {folder}.";
        if (skippedCount > 0)
        {
            report += $" {CountedNoun.Of(skippedCount, noun)} {WasOrWere(skippedCount)} skipped.";
        }

        return report;
    }

    // The past tense of "to be" that agrees with a count.
    private static string WasOrWere(int count)
    {
        return count == 1 ? "was" : "were";
    }
}
