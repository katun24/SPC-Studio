using Avalonia.Controls;
using SpcStudio.Audio;
using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio;

// Writing a song's samples out as files: one sample as a .brr or a .wav to a file the user picks and names, or every sample as a .brr into a folder the
// user picks.
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

    // Renders a sample to a .wav the user picks and names, as the Sample Editor's play button sounds it once through, saying so only where it could not
    // be written.
    public static async Task ExportOneToWav(Window owner, Sample sample)
    {
        var kind = new FileExportKind(WindowTitles.ExportSampleToWavPicker, WavWriter.DialogFilter, Sample.WavExtension, PickerId.ExportSampleToWav, AppPaths.SamplesFolder,
            WindowTitles.Failures.FailedToExportSampleToWav);
        var file = new ExportedFile(Path.ChangeExtension(sample.FileName, Sample.WavExtension), path => SampleWavExporter.Write(sample, path));
        await FileExport.WriteOne(owner, kind, file);
    }

    // Writes every sample in a song to a folder the user picks, one at a time, and reports what landed and what did not.
    public static async Task ExportAll(Window owner, Song song)
    {
        if (!CanExportAll(song))
        {
            return;
        }

        await ExportToFolder(owner, song.Samples);
    }

    // Writes some samples to a folder the user picks, one at a time, and reports what landed and what did not.
    public static Task ExportToFolder(Window owner, IEnumerable<Sample> samples)
    {
        var kind = new FolderExportKind(NameRefusals.SampleNoun, WindowTitles.SelectExportFolderPicker, PickerId.ExportAllSamples, AppPaths.SamplesFolder, WindowTitles.Notices.ExportSamples,
            WindowTitles.Successes.SamplesExported);
        return FolderExport.WriteAll(owner, kind, samples.Select(ExportedFileOf).ToList());
    }

    // A sample as an export writes it: its own file name, and its file bytes.
    private static ExportedFile ExportedFileOf(Sample sample)
    {
        return new ExportedFile(sample.FileName, path => File.WriteAllBytes(path, sample.FileBytes));
    }
}
