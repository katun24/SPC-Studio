using Avalonia.Controls;
using Avalonia.Interactivity;
using SpcStudio.Controls;
using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio;

// The song as a file: the title bar, the unsaved-changes question, new, open, the MML, SMWCentral and MIDI imports, save and save as, replacing the song with a file of any kind, and taking over
// a song wherever it came from.
public partial class MainWindow
{
    private const string UntitledSongName = "Untitled";
    private const string ImportingLine = "Importing MML...";
    private const string ImportButtonText = "Import";

    // Import From SMWCentral's box.
    private static readonly TextPrompt SmwcSongPrompt = SmwcSong.Prompt(WindowTitles.ImportFromSmwc, ImportButtonText);

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

    // Whether the song and its samples are exactly what its .txt holds, unchanged since it was imported from or last exported to it.
    private bool IsUnchangedSinceItsMml
    {
        get
        {
            return _mmlPath is not null && _history.MatchesMml && _sampleHistory.MatchesMml;
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

    // Offers to save before a file of a kind replaces the song, over a window of the caller's, then opens, imports or transcribes the file a step picks, answering whether one was picked
    // and tried.
    private async Task<bool> ReplaceSongWithFile(SongSourceKind kind, Func<Task<string?>> pickFile, Window? owner = null)
    {
        if (!await ConfirmDiscard(DiscardActionOf(kind), owner))
        {
            return false;
        }

        if (await pickFile() is not { } path)
        {
            return false;
        }

        await LoadSongSource(kind, path);
        return true;
    }

    // Offers to save before a file already known replaces the song, over a window of the caller's, then opens, imports or transcribes it, answering whether it was tried.
    private Task<bool> ReplaceSongWithKnownFile(SongSourceKind kind, string path, Window? owner = null)
    {
        return ReplaceSongWithFile(kind, () => Task.FromResult<string?>(path), owner);
    }

    // The words the save question names replacing the song with a file of a kind by.
    private static string DiscardActionOf(SongSourceKind kind)
    {
        return kind switch
        {
            SongSourceKind.SongFile => QuestionsAndNotices.OpenSongAction,
            SongSourceKind.Mml => QuestionsAndNotices.ImportMmlAction,
            SongSourceKind.Midi => QuestionsAndNotices.ImportMidiAction,
            SongSourceKind.Spc => QuestionsAndNotices.TranscribeFromMenuAction,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    // Opens, imports or transcribes a file by its kind, saying in a box why where it cannot.
    private Task LoadSongSource(SongSourceKind kind, string path)
    {
        return kind switch
        {
            SongSourceKind.SongFile => OpenSongFileOrSayWhy(path),
            SongSourceKind.Mml => ImportMmlFile(path),
            SongSourceKind.Midi => ImportMidiFile(path),
            SongSourceKind.Spc => TranscribeSpcFile(path),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    // An empty song at the zoom the grid is legible at, with the SMW instruments on.
    private static Song NewEmptySong()
    {
        var song = new Song { HorizontalZoom = 1 };
        song.TurnOnSmwInstruments();
        return song;
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
        await ReplaceSongWithFile(SongSourceKind.SongFile, () => FilePickers.OpenFile(this, WindowTitles.OpenSongPicker, SongFile.DialogFilter, PickerId.OpenSong, AppPaths.ProjectsFolder));
    }

    // Opens a song file, saying in a box where it could not be opened, and where its notes play SMW samples with no AddmusicK folder to read them from.
    private async Task OpenSongFileOrSayWhy(string path)
    {
        if (OpenSongFile(path) is { } failureMessage)
        {
            await MessageDialog.Show(this, failureMessage, WindowTitles.Failures.FailedToOpenSong);
            return;
        }

        if (_song.HasSmwSampleNotes && string.IsNullOrEmpty(AppPaths.AmkDirectory))
        {
            await MessageDialog.Show(this, QuestionsAndNotices.SmwSamplesWithoutAmkFolder, WindowTitles.Notices.NoAmkFolder);
        }
    }

    // Reads a song file and adopts it where the install's groups, read again, define its sample group, listing it among the recent files, answering why it could not be read or why its
    // group refuses it, or null where it was adopted.
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
        _recentFiles.Remember(path, SongSourceKind.SongFile);
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
        if (mmlPath is not null)
        {
            MarkMatchesMml();
        }

        LoadSmwSamples();
        Transport.ResetPlayback();
        UpdateTitle();
        ShowZoom();
        ShowPanelLayout();
        ShowSongState();
        RevealUsedLanes();
        ShowUndoState();
    }

    // Records that the song and its samples are exactly what its .txt now holds.
    private void MarkMatchesMml()
    {
        _history.MarkMatchesMml();
        _sampleHistory.MarkMatchesMml();
    }

    // Imports an AddmusicK .txt or a shared song's zip chosen in a picker, after offering to save.
    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        await ReplaceSongWithFile(SongSourceKind.Mml, () => FilePickers.OpenFile(this, WindowTitles.ImportMml, SongZip.MmlOrZipDialogFilter, PickerId.ImportMml, AppPaths.MusicFolder));
    }

    // Reads an AddmusicK .txt or a shared song's zip into the song, a zip being unpacked into the install first, and lists the file picked among the recent files.
    private async Task ImportMmlFile(string pickedPath)
    {
        await ImportMml(WindowTitles.ImportMml, MmlOrZip.StageCountOf(pickedPath), WindowTitles.Failures.FailedToExtractSong,
            (amkRoot, steps, _, cancel) => MmlOrZip.MmlPathOf(pickedPath, amkRoot, this, steps.Report, cancel), _ => pickedPath);
    }

    // Imports a song from SMWCentral by its number or its page's address typed into a box, after offering to save.
    private async void OnImportFromSmwcClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard(DiscardActionOf(SongSourceKind.Mml)))
        {
            return;
        }

        if (await SmwcSong.AskForNumber(this, SmwcSongPrompt) is not { } songNumber)
        {
            return;
        }

        await ImportMml(WindowTitles.ImportFromSmwc, SmwcSong.StageCount, WindowTitles.Failures.FailedToImportFromSmwc,
            (amkRoot, steps, setFailureTitle, cancel) => SmwcSong.MmlPathOf(songNumber, amkRoot, this, steps, setFailureTitle, cancel), mmlPath => mmlPath);
    }

    // Reads an AddmusicK .txt from any folder into the song against the AddmusicK folder set in Preferences, behind one progress box under a title, the .txt
    // found first by a step of a number of stages that may fetch or unpack it into that folder and that names the title its failures are reported under, a later
    // failure being reported as the import's; then reads the install's groups again, adopts the song where they define its sample group, lists it among the
    // recent files by the path a function makes of its .txt's and shows its import warnings.
    private async Task ImportMml(ProgressTitle progressTitle, int findingStageCount, FailureTitle findingFailureTitle,
        Func<string?, EqualStepsProgress, Action<FailureTitle>, CancellationToken, string> findMmlPath, Func<string, string> recentPathOf)
    {
        string? amkRoot = AppPaths.AmkDirectory;
        var warnings = new List<string>();
        var failureTitle = findingFailureTitle;
        int progressStepCount = findingStageCount + MmlImporter.StageCount;

        var importOutcome = await ProgressDialog.RunUnderOneLine(this, progressTitle, ImportingLine,
            (reportDoneFraction, cancel) =>
            {
                var steps = new EqualStepsProgress(progressStepCount, 0, reportDoneFraction);
                string mmlPath = findMmlPath(amkRoot, steps, title => failureTitle = title, cancel);
                failureTitle = WindowTitles.Failures.FailedToImportMml;
                return (Path: mmlPath, Song: MmlImporter.Load(mmlPath, amkRoot, warnings.Add, steps.Report, cancel));
            },
            ex => ex.Message);

        ReadAmkInstall();
        if (importOutcome.FailureMessage is { } failure)
        {
            await MessageDialog.Show(this, failure, failureTitle);
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
        _recentFiles.Remember(recentPathOf(importOutcome.Value.Path), SongSourceKind.Mml);
        await ShowImportWarnings(warnings);
    }

    // Imports a MIDI file chosen in a picker, after offering to save.
    private async void OnImportMidiClick(object? sender, RoutedEventArgs e)
    {
        await ReplaceSongWithFile(SongSourceKind.Midi, () => FilePickers.OpenFile(this, WindowTitles.ImportMidi, MidiImporter.DialogFilter, PickerId.ImportMidi, null));
    }

    // Reads a MIDI file into the song, saying in a box why it could not be imported, lists it among the recent files and shows its import warnings.
    private async Task ImportMidiFile(string path)
    {
        var warnings = new List<string>();
        Song? song = null;
        if (!await MessageDialog.TryOrSayFailure(this, () => song = MidiImporter.Load(path, AppPaths.AmkDirectory, warnings.Add), WindowTitles.Failures.FailedToImportMidi) || song is null)
        {
            return;
        }

        AdoptSong(song, songPath: null, mmlPath: null);
        _recentFiles.Remember(path, SongSourceKind.Midi);
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

    // Writes the song with the view it is shown at to a file, makes it the song's file and lists it among the recent files, answering whether it was written.
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
        _recentFiles.Remember(path, SongSourceKind.SongFile);
        return true;
    }

    // Closes the window, which asks about unsaved changes.
    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
