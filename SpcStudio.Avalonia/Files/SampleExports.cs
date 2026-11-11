using Avalonia.Controls;
using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio;

// Writing a song's samples out as .brr files: one sample to a file the user picks and names, or every sample into a folder the user picks.
internal static class SampleExports
{
    // Whether a song has any sample for an export of every sample to write.
    public static bool CanExportAll(Song song)
    {
        return song.Samples.Count > 0;
    }

    // Writes a sample to a file the user picks and names, saying so only where it could not be written.
    public static async Task ExportOne(Window owner, Sample sample)
    {
        var kind = new FileExportKind(WindowTitles.ExportSample, Sample.DialogFilter, Sample.BrrExtension, PickerId.ExportSample, AppPaths.SamplesFolder,
            WindowTitles.Failures.FailedToExportSample);
        await FileExport.WriteOne(owner, kind, ExportedFileOf(sample));
    }

    // Writes every sample in a song to a folder the user picks, one at a time, and reports what landed and what did not.
    public static async Task ExportAll(Window owner, Song song)
    {
        if (!CanExportAll(song))
        {
            return;
        }

        var kind = new FolderExportKind(NameRefusals.SampleNoun, WindowTitles.SelectExportFolderPicker, PickerId.ExportAllSamples, AppPaths.SamplesFolder, WindowTitles.Notices.ExportSamples,
            WindowTitles.Successes.SamplesExported);
        await FolderExport.WriteAll(owner, kind, song.Samples.Select(ExportedFileOf).ToList());
    }

    // A sample as an export writes it: its own file name, and its file bytes.
    private static ExportedFile ExportedFileOf(Sample sample)
    {
        return new ExportedFile(sample.FileName, path => File.WriteAllBytes(path, sample.FileBytes));
    }
}
