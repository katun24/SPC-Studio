using Avalonia.Controls;
using Avalonia.Interactivity;
using SpcStudio.Controls;
using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio;

// The song as a file: the title bar, the unsaved-changes question, new, open, the MML and MIDI imports, save and save as, and taking over a song wherever it came from.
public partial class MainWindow
{
    private const string UntitledSongName = "Untitled";
    private const string ImportingLine = "Importing MML...";

    // The song's file name for the save question and the names an export suggests, or the word for a song never saved.
    private string SongName
    {
        get
        {
            return _songPath is null ? UntitledSongName : Path.GetFileName(_songPath);
        }
    }

    // The title in the Song panel's field as it stands, without the spaces around it, empty where there is none.
    private string ShownSongTitle
    {
        get
        {
            return TrimmedText(SongTitleBox);
        }
    }

    // Whether the song or its samples hold edits the file on disk does not.
    private bool IsModified
    {
        get
        {
            return _history.IsModified || _sampleHistory.IsModified;
        }
    }

    // Writes the title bar and counts the ARAM and the song's length again.
    private void UpdateTitle()
    {
        ShowTitleBar();
        ShowAramTotals();
        ShowPlaybackTime();
    }

    // Writes the title bar: the modified marker, the app and the title in the Song panel's field, or the word for an untitled song.
    private void ShowTitleBar()
    {
        string songTitle = ShownSongTitle;
        string windowTitle = WindowTitles.WithDocument(WindowTitles.App, songTitle.Length > 0 ? songTitle : UntitledSongName);
        Title = IsModified ? WindowTitles.Modified(windowTitle) : windowTitle;
    }

    // Offers to save before an action that throws the song away, over a window of the caller's, answering whether the action may go on.
    private async Task<bool> ConfirmDiscard(string action, Window? owner = null)
    {
        if (!IsModified)
        {
            return true;
        }

        return await MessageDialog.AskToSaveBefore(owner ?? this, SongName, action, SaveSong);
    }

    // An empty song at the zoom the grid is legible at.
    private static Song NewEmptySong()
    {
        return new Song { HorizontalZoom = 1 };
    }

    // Replaces the song with an empty one shown from its start, after offering to save.
    private async void OnNewSong(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard(QuestionsAndNotices.NewSongAction))
        {
            return;
        }

        AdoptSong(NewEmptySong(), songPath: null, mmlPath: null);
        ScrollToSongStart();
    }

    // Opens a song file chosen in a picker, after offering to save, saying in a box where it could not be opened.
    private async void OnOpenSong(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard(QuestionsAndNotices.OpenSongAction))
        {
            return;
        }

        string? chosen = await FilePickers.OpenFile(this, WindowTitles.OpenSongPicker, SongFile.DialogFilter, PickerId.OpenSong, AppPaths.ProjectsFolder);
        if (chosen is null)
        {
            return;
        }

        await OpenSongFileOrSayWhy(chosen);
    }

    // Opens a song file, saying in a box where it could not be opened.
    private async Task OpenSongFileOrSayWhy(string path)
    {
        if (OpenSongFile(path) is { } failureMessage)
        {
            await MessageDialog.Show(this, failureMessage, WindowTitles.Failures.FailedToOpenSong);
        }
    }

    // Reads a song file and adopts it where the install's groups, read again, define its sample group, answering why it could not be read or why its group refuses it, or null where it was adopted.
    private string? OpenSongFile(string path)
    {
        Song song;
        try
        {
            song = SongFile.Load(path);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        ReadAmkInstall();
        if (MissingSampleGroupSentence(song) is { } missingMessage)
        {
            return missingMessage;
        }

        AdoptSong(song, path, mmlPath: null);
        return null;
    }

    // Hands the song to the roll, the ruler, the markers and every lane.
    private void HandSongToGrid()
    {
        Roll.Song = _song;
        Ruler.Song = _song;
        Markers.Song = _song;
        foreach (var lane in _lanes)
        {
            lane.Song = _song;
        }
    }

    // Takes over a song wherever it came from, with the file Save writes back to and the MML file it was imported from, and shows it from its first channel in every control.
    private void AdoptSong(Song song, string? songPath, string? mmlPath)
    {
        StopPlayback();
        StopNoteRun();
        _song = song;
        HandSongToGrid();
        Roll.SelectedChannel = 0;
        _currentInstrumentId = null;
        _songPath = songPath;
        _mmlPath = mmlPath;
        _history.Reset(_song);
        _sampleHistory.Reset(_song);
        Transport.ResetPlayback();
        UpdateTitle();
        ShowZoom();
        ShowPanelLayout();
        ShowSongState();
        RevealUsedLanes();
        ShowUndoState();
    }

    // Imports an AddmusicK .txt or a shared song's zip chosen in a picker, after offering to save.
    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard(QuestionsAndNotices.ImportMmlAction))
        {
            return;
        }

        string? chosen = await FilePickers.OpenFile(this, WindowTitles.ImportMml, SongZip.MmlOrZipDialogFilter, PickerId.ImportMml, AppPaths.MusicFolder);
        if (chosen is null)
        {
            return;
        }

        await ImportMmlFile(chosen);
    }

    // Reads an AddmusicK .txt into the song behind one progress box, a zip being unpacked into the install first and a failure to unpack it reported under its own title, then reads the install's
    // groups again and adopts the song where they define its sample group, and shows its import warnings.
    private async Task ImportMmlFile(string pickedPath)
    {
        string? amkRoot = AppPaths.AmkDirectory;
        var warnings = new List<string>();
        bool isMmlLocated = false;
        int progressStepCount = MmlOrZip.StageCountOf(pickedPath) + MmlImporter.StageCount;

        var importOutcome = await ProgressDialog.RunUnderOneLine(this, WindowTitles.ImportMml, ImportingLine,
            (reportDoneFraction, cancel) =>
            {
                var steps = new EqualStepsProgress(progressStepCount, 0, reportDoneFraction);
                string mmlPath = MmlOrZip.MmlPathOf(pickedPath, amkRoot, steps.Report, cancel);
                isMmlLocated = true;
                return (Path: mmlPath, Song: MmlImporter.Load(mmlPath, warnings.Add, steps.Report, cancel));
            },
            ex => ex.Message);

        ReadAmkInstall();
        if (importOutcome.FailureMessage is { } failure)
        {
            await MessageDialog.Show(this, failure, isMmlLocated ? WindowTitles.Failures.FailedToImportMml : WindowTitles.Failures.FailedToExtractSong);
            return;
        }

        if (importOutcome.Value.Song is not { } song)
        {
            return;
        }

        if (MissingSampleGroupSentence(song) is { } missingMessage)
        {
            await MessageDialog.Show(this, missingMessage, WindowTitles.Failures.FailedToImportMml);
            return;
        }

        AdoptSong(song, songPath: null, importOutcome.Value.Path);
        await ShowImportWarnings(warnings);
    }

    // Imports a MIDI file chosen in a picker, after offering to save, saying in a box why it could not be imported, and shows its import warnings.
    private async void OnImportMidiClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard(QuestionsAndNotices.ImportMidiAction))
        {
            return;
        }

        string? chosen = await FilePickers.OpenFile(this, WindowTitles.ImportMidi, MidiImporter.DialogFilter, PickerId.ImportMidi, null);
        if (chosen is null)
        {
            return;
        }

        var warnings = new List<string>();
        Song? song = null;
        if (!await MessageDialog.TryOrSayFailure(this, () => song = MidiImporter.Load(chosen, AppPaths.AmkDirectory, warnings.Add), WindowTitles.Failures.FailedToImportMidi) || song is null)
        {
            return;
        }

        AdoptSong(song, songPath: null, mmlPath: null);
        await ShowImportWarnings(warnings);
    }

    // Shows an import's warnings together in one box, where it gave any.
    private async Task ShowImportWarnings(List<string> warnings)
    {
        if (warnings.Count > 0)
        {
            await MessageDialog.Show(this, string.Join(MessageDialog.ParagraphBreak, warnings), WindowTitles.Notices.ImportedWithDifferences);
        }
    }

    // Saves the song from the File menu or its key.
    private async void OnSaveSong(object? sender, RoutedEventArgs e)
    {
        await SaveSong();
    }

    // Saves the song under a name chosen in a picker.
    private async void OnSaveSongAs(object? sender, RoutedEventArgs e)
    {
        await SaveSongAs();
    }

    // Saves to the song's file, asking for one where it has never been saved, answering whether it was saved.
    private async Task<bool> SaveSong()
    {
        if (_songPath is null)
        {
            return await SaveSongAs();
        }

        return await WriteSong(_songPath);
    }

    // Saves the song to a file chosen in a picker, answering whether it was saved.
    private async Task<bool> SaveSongAs()
    {
        // The name the picker offers: the song file's own, or the word for an untitled song.
        string suggestedName = _songPath is null ? UntitledSongName + SongFile.Extension : Path.GetFileName(_songPath);

        string? chosen = await FilePickers.SaveFile(this, WindowTitles.SaveSongAsPicker, SongFile.DialogFilter, suggestedName, SongFile.Extension, PickerId.SaveSong, AppPaths.ProjectsFolder);
        return chosen is not null && await WriteSong(chosen);
    }

    // Writes the song with the view it is shown at to a file and makes it the song's file, answering whether it was written.
    private async Task<bool> WriteSong(string path)
    {
        _song.HorizontalZoom = RollMetrics.Zoom;
        _song.VerticalZoom = RollMetrics.VerticalZoom;
        _song.LaneHeights = CaptureLaneHeights();
        _song.LaneOrder = CaptureLaneOrder();
        CapturePanelLayout();

        if (!await MessageDialog.TryOrSayFailure(this, () => SongFile.Save(path, _song), WindowTitles.Failures.FailedToSaveSong))
        {
            return false;
        }

        _songPath = path;
        _history.MarkSaved();
        _sampleHistory.MarkSaved();
        UpdateTitle();
        return true;
    }

    // Closes the window, which asks about unsaved changes.
    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
