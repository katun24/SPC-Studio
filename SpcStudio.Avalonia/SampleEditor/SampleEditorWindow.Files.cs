using Avalonia.Interactivity;
using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio;

// The sample editor's file buttons: the four that ask the main window to change the song's samples, and the two that write samples out to a folder.
public partial class SampleEditorWindow
{
    // Asks for an empty sample to be added to the song.
    private void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        CreateRequested?.Invoke();
    }

    // Asks for files to be imported into the song as samples.
    private void OnImportClick(object? sender, RoutedEventArgs e)
    {
        ImportRequested?.Invoke();
    }

    // Asks for a second sample holding the showing sample's audio to be added to the song.
    private void OnDuplicateClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedSampleId is { } id)
        {
            DuplicateRequested?.Invoke(id);
        }
    }

    // Turns the drop-down into a field holding the showing sample's name.
    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (CurrentSample is { } sample)
        {
            SamplePick.StartRename(sample.Name);
        }
    }

    // Asks for the sample showing to take the name its rename finished with, where it differs from its own.
    private void OnSampleNameCommitted(string name)
    {
        if (CurrentSample is { } sample && name != sample.Name)
        {
            RenameRequested?.Invoke(sample.Id, name);
        }
    }

    // Asks for the sample showing to be taken out of the song.
    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedSampleId is { } id)
        {
            DeleteRequested?.Invoke(id);
        }
    }

    // Writes the sample showing to a file the user picks and names, saying so only where it could not be written.
    private async void OnExportSampleClick(object? sender, RoutedEventArgs e)
    {
        if (CurrentSample is not { } sample)
        {
            return;
        }

        var kind = new FileExportKind(WindowTitles.ExportSample, Sample.DialogFilter, Sample.BrrExtension, PickerId.ExportSample, AppPaths.SamplesFolder,
            WindowTitles.Failures.FailedToExportSample);
        await FileExport.WriteOne(this, kind, ExportedFileOf(sample));
    }

    // Writes every sample in the song to a folder the user picks, one at a time, and reports what landed and what did not.
    private async void OnExportAllClick(object? sender, RoutedEventArgs e)
    {
        if (_song is not { } song || song.Samples.Count == 0)
        {
            return;
        }

        var kind = new FolderExportKind(NameRefusals.SampleNoun, WindowTitles.SelectExportFolderPicker, PickerId.ExportAllSamples, AppPaths.SamplesFolder, WindowTitles.Notices.ExportSamples,
            WindowTitles.Successes.SamplesExported);
        await FolderExport.WriteAll(this, kind, song.Samples.Select(ExportedFileOf).ToList());
    }

    // A sample as an export writes it: its own file name, and its file bytes.
    private static ExportedFile ExportedFileOf(Sample sample)
    {
        return new ExportedFile(sample.FileName, path => File.WriteAllBytes(path, sample.FileBytes));
    }
}
