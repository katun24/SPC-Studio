using Avalonia.Controls;
using Avalonia.Interactivity;
using SpcStudio.Audio;

namespace SpcStudio;

// The sample editor's file buttons and the Sample menu that does their work: the four that ask the main window to change the song's samples, and the
// two that write samples out to a folder.
public partial class SampleEditorWindow
{
    // Each item of the Sample menu with the button whose work it does.
    private (MenuItem Item, Button Button)[] SampleMenuButtons
    {
        get
        {
            return
            [
                (NewSampleItem, CreateButton),
                (ImportSamplesItem, ImportButton),
                (ExportSampleItem, ExportSampleButton),
                (ExportSampleToWavItem, ExportSampleButton),
                (ExportAllSamplesItem, ExportAllButton),
                (DuplicateSampleItem, DuplicateButton),
                (CreateChordSampleItem, DuplicateButton),
                (DeleteSampleItem, DeleteButton),
            ];
        }
    }

    // Grays each item of the Sample menu whose button is hidden or grayed, as the menu opens.
    private void OnSampleMenuOpened(object? sender, RoutedEventArgs e)
    {
        foreach (var (item, button) in SampleMenuButtons)
        {
            item.IsEnabled = button.IsVisible && button.IsEnabled;
        }
    }

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

    // Stops the preview and opens the Create Chord Sample window over the sample showing, once every edit asked for has been made.
    private async void OnCreateChordSampleClick(object? sender, RoutedEventArgs e)
    {
        StopPreview();
        await ActOnSampleShowing(CreateChordSampleOver);
    }

    // Opens the Create Chord Sample window over a sample, and asks for the chord it creates to be added to the song.
    private async Task CreateChordSampleOver(Sample sample)
    {
        if (await CreateChordSampleWindow.Show(this, sample, _previewMixer, LoopOptions.CrossfadeLength.Fraction) is { } chord)
        {
            ChordSampleCreated?.Invoke(chord);
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
        await ActOnSampleShowing(sample => SampleExports.ExportOne(this, sample));
    }

    // Renders the sample showing to a .wav the user picks and names, saying so only where it could not be written.
    private async void OnExportSampleToWavClick(object? sender, RoutedEventArgs e)
    {
        await ActOnSampleShowing(sample => SampleExports.ExportOneToWav(this, sample));
    }

    // Hands the sample showing to an act, once every edit asked for has been made.
    private async Task ActOnSampleShowing(Func<Sample, Task> act)
    {
        await _sampleEdits.WhenEmpty();
        if (CurrentSample is not { } sample)
        {
            return;
        }

        await act(sample);
    }

    // Writes every sample in the song to a folder the user picks, one at a time, once every edit asked for has been made, and reports what landed and what
    // did not.
    private async void OnExportAllClick(object? sender, RoutedEventArgs e)
    {
        await _sampleEdits.WhenEmpty();
        if (_song is not { } song)
        {
            return;
        }

        await SampleExports.ExportAll(this, song);
    }
}
