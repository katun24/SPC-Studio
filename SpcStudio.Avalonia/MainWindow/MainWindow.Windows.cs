using Avalonia.Interactivity;
using Avalonia.Controls;

using SpcStudio.Audio;
using SpcStudio.Export;
using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio;

// The main window's secondary windows - the Sample Editor, the SPC Player, the Instrument Picker and the song list - and what the SPC Player hands
// back: samples and instruments extracted into the song, auditions of what it points at, and transcriptions.
public partial class MainWindow
{
    private const string TranscribingLine = "Transcribing song from SPC...";

    private SpcPlayerWindow? _spcPlayer;
    private SongListWindow? _songList;
    private PanelPlacement _spcPlayerPanelPlacement = new();

    // The window a box about an SPC Player extract stands over: the player, where the press was, or this window where the player has closed.
    private Window ExtractBoxOwner
    {
        get
        {
            return (Window?)_spcPlayer ?? this;
        }
    }

    // Opens the sample editor on the current instrument's sample, or on the song's first where it plays none.
    private void OnSampleEditorClick(object? sender, RoutedEventArgs e)
    {
        ShowSampleEditor(CurrentInstrument is { } instrument ? _song.SampleFor(instrument)?.Id : null);
    }

    // Opens the sample editor on the sample the instrument panel's Sample row is showing.
    private void OnInstrumentSampleEdit(int sampleId)
    {
        ShowSampleEditor(sampleId);
    }

    // Opens the sample editor on a sample, or points the one already open at it and raises it.
    private void ShowSampleEditor(int? sampleId)
    {
        if (_sampleEditor is not null)
        {
            _sampleEditor.ShowSamples(_song, sampleId);
            _sampleEditor.Activate();
            return;
        }

        _sampleEditor = CreateSampleEditor();
        _sampleEditor.SetPreviewMixer(_audio?.Mixer);
        _sampleEditor.ShowSamples(_song, sampleId);
        ShowBeside(_sampleEditor);
    }

    // A sample editor with every request it raises wired to this window, forgotten here as it closes.
    private SampleEditorWindow CreateSampleEditor()
    {
        var editor = new SampleEditorWindow();
        editor.Closed += (_, _) => _sampleEditor = null;
        editor.LoopChanged += OnSampleLoopChanged;
        editor.LoopBlockChanged += OnSampleLoopBlockChanged;
        editor.PointsReplaced += OnSamplePointsReplaced;
        editor.PointsKept += OnSamplePointsKept;
        editor.PointsOverwritten += OnSamplePointsOverwritten;
        editor.LoopFound += OnSampleLoopFound;
        editor.EffectsChanged += OnSampleEffectsChanged;
        editor.PreviewPitchChanged += OnSamplePreviewPitchChanged;
        editor.RateConverted += OnSampleRateConverted;
        editor.UndoRequested += OnSampleUndo;
        editor.RedoRequested += OnSampleRedo;
        editor.CreateRequested += OnSampleCreateRequested;
        editor.DuplicateRequested += OnSampleDuplicateRequested;
        editor.DeleteRequested += OnSampleDeleteRequested;
        editor.RenameRequested += OnSampleRenameRequested;
        editor.ImportRequested += async () => await ImportSamples(editor);
        return editor;
    }

    // Opens the SPC Player, or raises the one already open.
    private void OnSpcPlayerClick(object? sender, RoutedEventArgs e)
    {
        if (_spcPlayer is not null)
        {
            _spcPlayer.Activate();
            return;
        }

        _spcPlayer = CreateSpcPlayer();
        ShowBeside(_spcPlayer);
    }

    // An SPC Player with every request it raises wired to this window, forgotten here as it closes.
    private SpcPlayerWindow CreateSpcPlayer()
    {
        var player = new SpcPlayerWindow(() => FollowPlaybackItem.IsChecked, _spcPlayerPanelPlacement);
        player.Closing += (_, _) => _spcPlayerPanelPlacement = player.CaptureSidePanelPlacement();
        player.Closed += (_, _) => _spcPlayer = null;
        player.SampleExtracted += async brrBytes => await ExtractSample(brrBytes);
        player.InstrumentExtracted += async (brrBytes, envelope) => await ExtractInstrument(brrBytes, envelope);
        player.TranscribeRequested += async (file, trace, endFrame) => await TranscribeFromPlayer(file, trace, endFrame);
        return player;
    }

    // Opens the song list over the install Preferences names, or raises the one already open, saying in a box why not where the list cannot be read.
    private async void OnSongListClick(object? sender, RoutedEventArgs e)
    {
        if (_songList is not null)
        {
            _songList.Activate();
            return;
        }

        string? amkRoot = AppPaths.AmkDirectory;
        if (string.IsNullOrEmpty(amkRoot) || !Directory.Exists(amkRoot))
        {
            await MessageDialog.Show(this, AmkRunException.NoAmkFolder().Message, WindowTitles.Failures.FailedToOpenSongList);
            return;
        }

        AmkSongList list;
        try
        {
            list = AmkSongList.Read(amkRoot);
        }
        catch (AmkSongListException exception)
        {
            await MessageDialog.Show(this, exception.Message, WindowTitles.Failures.FailedToOpenSongList);
            return;
        }

        _songList = new SongListWindow(amkRoot, list);
        _songList.Closed += (_, _) => _songList = null;
        _songList.LoadRequested += async path => await LoadFromSongList(path);
        ShowBeside(_songList);
    }

    // Imports a song the song list's row plays, after offering to save over the song list.
    private async Task LoadFromSongList(string path)
    {
        if (!await ConfirmDiscard(QuestionsAndNotices.ImportMmlAction, _songList))
        {
            return;
        }

        await ImportMmlFile(path);
    }

    // Opens the Instrument Picker from the Windows menu.
    private void OnInstrumentPickerClick(object? sender, RoutedEventArgs e)
    {
        ShowInstrumentPicker();
    }

    // Opens the Instrument Picker, or raises the one already open.
    private void ShowInstrumentPicker()
    {
        if (_picker is not null)
        {
            _picker.Activate();
            return;
        }

        _picker = CreateInstrumentPicker();
        _picker.ShowFolders(_pickerOpenFolders);
        ShowBeside(_picker);
    }

    // An Instrument Picker with every request it raises wired to this window, forgotten here as it closes.
    private InstrumentPickerWindow CreateInstrumentPicker()
    {
        var picker = new InstrumentPickerWindow(_pickerHistory);
        picker.Closed += (_, _) => _picker = null;
        picker.AddRequested += async paths => await AddPickedInstruments(picker, paths);
        picker.AuditionRequested += AuditionInstrumentFile;
        picker.AuditionReleased += ReleaseAudition;
        return picker;
    }

    // Reads picked instrument files into the song as Import does, and brings the instrument panel to the front.
    private async Task AddPickedInstruments(Window owner, IReadOnlyList<string> paths)
    {
        if (await ImportInstrumentFiles(owner, paths) is null)
        {
            return;
        }

        ShowPanel(PanelTab.Instrument);
    }

    // Sounds an instrument file's instrument at its audition pitch through a copy of the song, for as long as the picker's button is held.
    private void AuditionInstrumentFile(InstrumentFile file)
    {
        var songCopy = _song.Copy();

        Instrument instrument;
        try
        {
            instrument = file.AddTo(songCopy);
        }
        catch (InvalidDataException)
        {
            return;
        }

        AuditionInstrumentAtAuditionPitch(songCopy, instrument);
    }

    // Closes every secondary window that is open, the panel windows closed rather than docked.
    private void CloseSecondaryWindows()
    {
        _sampleEditor?.Close();
        _spcPlayer?.Close();
        _songList?.Close();
        _picker?.Close();
        foreach (var slot in PanelSlots)
        {
            slot.Window?.CloseWithoutDocking();
        }

        _groupWindow?.CloseWithoutDocking();
    }

    // Puts a sample the SPC Player handed over into the song, and says so over the player.
    private async Task ExtractSample(byte[] brrBytes)
    {
        if (await AddExtractedSample(brrBytes) is not { } sample)
        {
            return;
        }

        ShowChangedSampleList();
        await ReportExtracted(sample.Name, WindowTitles.Successes.SampleAdded);
    }

    // Puts a sample the SPC Player handed over into the song with an instrument on it under the envelope it sent, selects the instrument, and says so
    // over the player.
    private async Task ExtractInstrument(byte[] brrBytes, EnvelopeState envelope)
    {
        if (await AddExtractedSample(brrBytes) is not { } sample)
        {
            return;
        }

        _sampleHistory.Discard(_song);
        var instrument = _song.CreateNumberedInstrument(sample);
        instrument.Envelope = envelope;
        SelectAddedInstrument(instrument);
        await ReportExtracted(instrument.Name, WindowTitles.Successes.InstrumentAdded);
    }

    // Says over the SPC Player that what it handed over was added to the song under a name.
    private async Task ReportExtracted(string name, SuccessTitle title)
    {
        await MessageDialog.Show(ExtractBoxOwner, QuestionsAndNotices.AddedToSong(name), title);
    }

    // Adds an extracted sample to the song under a free name, or says over the player why its bytes were refused.
    private async Task<Sample?> AddExtractedSample(byte[] brrBytes)
    {
        Sample sample;
        try
        {
            sample = Sample.FromBytes(_song.FreeSampleName(), brrBytes);
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(ExtractBoxOwner, ex.Message, WindowTitles.Failures.FailedToExtractSample);
            return null;
        }

        return _song.AddSample(sample);
    }

    // Replaces the song with a transcription of the SPC Player's dump up to a frame, once the open song may be discarded.
    private async Task TranscribeFromPlayer(SpcFile file, SpcTrace trace, long endFrame)
    {
        if (!await ConfirmDiscard(QuestionsAndNotices.TranscribeFromPlayerAction, _spcPlayer))
        {
            return;
        }

        await TranscribeBehindProgress((Window?)_spcPlayer ?? this,
            (reportDoneFraction, cancel) => TranscribeTrace(file, trace.EndingAt(endFrame, cancel), SpcTranscribe.StageCount, 0, reportDoneFraction, cancel));
    }

    // Picks an .spc, plays it through and replaces the song with its transcription, the SPC Player left out.
    private async void OnTranscribeSpcClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard(QuestionsAndNotices.TranscribeFromMenuAction))
        {
            return;
        }

        if (await SpcFileOpening.PickAndRead(this, WindowTitles.TranscribeFromSpcPicker) is not var (_, file))
        {
            return;
        }

        const int progressStepCount = SpcTrace.MaxPassCount + SpcTranscribe.StageCount;
        await TranscribeBehindProgress(this, (reportDoneFraction, cancel) =>
        {
            var passes = new EqualStepsProgress(progressStepCount, 0, reportDoneFraction);
            var trace = SpcTrace.Run(file.FileBytes, passes.Report, cancel);
            return TranscribeTrace(file, trace, progressStepCount, SpcTrace.MaxPassCount, reportDoneFraction, cancel);
        });
    }

    // Transcribes a traced dump into a song, its stages counted as steps of a whole job from a given step on.
    private static Song TranscribeTrace(SpcFile file, SpcTrace trace, int stepCount, int firstStepIndex, Action<double> reportDoneFraction, CancellationToken cancel)
    {
        var stages = new EqualStepsProgress(stepCount, firstStepIndex, reportDoneFraction);
        return SpcTranscribe.ToSong(file, trace, progress: stages.Report, cancel: cancel);
    }

    // Runs a transcription behind one progress box and opens its song in place of the song, a failure shown over a given window.
    private async Task TranscribeBehindProgress(Window failureOwner, Func<Action<double>, CancellationToken, Song> transcribe)
    {
        var run = await ProgressDialog.RunUnderOneLine(this, WindowTitles.TranscribingFromSpc, TranscribingLine, transcribe, ex => ex.Message);

        if (run.FailureMessage is { } failure)
        {
            await MessageDialog.Show(failureOwner, failure, WindowTitles.Failures.FailedToTranscribeSpc);
            return;
        }

        if (run.Value is not { } song)
        {
            return;
        }

        AdoptSong(song, songPath: null, mmlPath: null);
    }

    // What a transcription says once its song is open: the tempo it was placed at and how often that changes, then each warning.
    private static string TranscriptionReport(Song song, IEnumerable<string> warnings)
    {
        string tempoParagraph = $"Transcribed at tempo {song.BaseTempo}, which is about {SnesTiming.TempoToBpm(song.BaseTempo):F0} beats a minute."
            + TempoChangesSentence(song.TempoChanges.Count);
        string[] paragraphs = [tempoParagraph, .. warnings];
        return string.Join(MessageDialog.ParagraphBreak, paragraphs);
    }

    // The sentence saying how often a transcribed song's tempo changes, empty where it never does.
    private static string TempoChangesSentence(int changeCount)
    {
        return changeCount switch
        {
            0 => "",
            1 => " The tempo changes once in the song.",
            _ => $" The tempo changes {changeCount} times in the song.",
        };
    }
}
