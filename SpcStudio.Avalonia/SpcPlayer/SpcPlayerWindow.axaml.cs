using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Import;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio;

// The SPC Player: an .spc from any game opened and played on a device of its own, its eight voices and its sample directory read out live, the whole
// song drawn as a canvas, and its samples and instruments handed to the main window to take into the song.
public partial class SpcPlayerWindow : StandaloneWindow
{
    private const string LoadingLine = "Loading SPC...";
    private const int MinPositionRailFrames = 1;
    private const int NoEntry = -1;
    private const int SampleLoopColumn = 2;
    private const int VoiceEchoColumn = 7;
    private const int VoiceNoiseColumn = 8;
    private const string VolumeSidesSeparator = " / ";
    private const string EchoWritesOnText = "On";
    private const string EchoWritesOffText = "Off";
    private const string ListColumnWidthResource = "SpcListColumnWidth";
    private const string ListColumnMinWidthResource = "SpcListColumnMinWidth";

    private static readonly TimeSpan ReadoutInterval = TimeSpan.FromMilliseconds(100);

    // The sample list's columns: the entry's number, its length in blocks and whether it loops.
    private static readonly ListTableColumn[] SampleListColumns =
    [
        ListTableColumn.OfNumbers("#", SpcSampleDirectory.MaxEntries),
        ListTableColumn.OfNumbers("Length", SpcSampleDirectory.MaxBlocks),
        ListTableColumn.OfIcon("Loop"),
    ];

    // The voice list's columns: the voice's number, the entry it plays, its envelope registers, its pitch, its envelope's level, its two volumes and
    // whether it sends into the echo and plays the noise.
    private static readonly ListTableColumn[] VoiceListColumns =
    [
        ListTableColumn.OfNumbers("#", SnesDspRegister.VoiceCount),
        ListTableColumn.OfNumbers("Sample", SpcSampleDirectory.MaxEntries),
        ListTableColumn.OfHexNumbers("ADSR", AdsrText(byte.MaxValue, byte.MaxValue)),
        ListTableColumn.OfHexNumbers("GAIN", AmkCommands.HexSpelling(byte.MaxValue)),
        ListTableColumn.OfHexNumbers("Pitch", PitchText(SnesPitch.MaxPitchRegister)),
        ListTableColumn.OfNumbers("ENVX", SnesDspRegister.MaxEnvelopeLevel),
        ListTableColumn.OfTexts("Volume", ListTableColumn.DigitFills(-sbyte.MinValue, ListTableColumn.DecimalBase, side => VolumeText(-side, -side))),
        ListTableColumn.OfIcon(SettingLabels.EchoSend),
        ListTableColumn.OfIcon("Noise"),
    ];

    private readonly DispatcherTimer _readoutTimer;
    private readonly DispatcherTimer _playheadTimer;
    private readonly PlayheadFollower _traceFollower;
    private readonly ScrollViewerView _traceView;
    private readonly EdgeScroll _edgeScroll;
    private readonly Func<bool> _isFollowPlaybackOn = () => false;
    private readonly MuteAndSolo _voiceMuting = new(SnesDspRegister.VoiceCount);
    private readonly MuteAndSolo _sampleMuting = new(SpcSampleDirectory.MaxEntries);
    private readonly ChannelButton[] _channelButtons = new ChannelButton[SnesDspRegister.VoiceCount];
    private readonly Border[] _voiceRows = new Border[SnesDspRegister.VoiceCount];
    private readonly TextBlock[][] _voiceCells = new TextBlock[SnesDspRegister.VoiceCount][];
    private readonly Panel[] _voiceButtonSlots = new Panel[SnesDspRegister.VoiceCount];
    private readonly List<Border> _sampleRows = [];
    private readonly List<TextBlock[]> _sampleCells = [];
    private readonly List<MuteSoloPair> _sampleToggles = [];
    private SpcFile? _openFile;
    private SpcEngine? _engine;
    private int? _selectedEntryIndex;
    private int? _selectedVoice;
    private ChannelButton _stackedButton = null!;
    private PanelWindow? _sidePanelWindow;
    private SpcDeck? _sidePanelWindowDeck;

    // Tripped when the window closes, which stops a reading of the song still running.
    private readonly CancellationTokenSource _closing = new();

    // The directory entries not silenced that a voice not silenced played as a sample from the last readout's playhead to this one's.
    private readonly HashSet<int> _soundingEntryIndexes = [];

    // Each sample's bright and dull text brushes in the Samples table, made once per file and held.
    private readonly Dictionary<int, (IBrush Bright, IBrush Dull)> _textBrushesOfSrcn = [];

    // The directory entry each voice row is showing, or none where the voice has nothing loaded.
    private readonly int[] _voiceEntryIndexes = new int[SnesDspRegister.VoiceCount];

    // A second emulator on the same file and a device of its own, which plays a voice row's auditions.
    private SpcEngine? _auditionEngine;

    // A device of its own and the mixer feeding it, which plays a sample row's auditions.
    private AudioEngine? _sampleAuditionAudio;

    // What the canvas draws, found by playing the file through when it was opened.
    private SpcTrace? _trace;

    // The chip as the last readout found it, which the two adds and every audition read.
    private SpcFile? _shownState;

    // The entries the Samples table lists, as the last readout found them, in table order.
    private IReadOnlyList<SpcDirectoryEntry> _directoryEntries = [];

    // Where the playhead stood at the last readout, or null where it has jumped since.
    private long? _lastReadoutPosition;

    // Where the side panel's own window is to stand once the first file has been read through, or null where the side panel opens in this window.
    private PanelPlacement? _sidePanelPlacementToRestore;

    // Builds the lists, the channel column and the transport, hooks the canvas and the keys, and gives the device back when the window closes.
    public SpcPlayerWindow()
    {
        InitializeComponent();

        Body.Margin = new Thickness(Body.Margin.Left, Body.Margin.Top, Body.Margin.Right, 0);
        SampleTable.Columns = SampleListColumns;
        VoiceTable.Columns = VoiceListColumns;
        BuildVoiceRows();
        HookTransport();
        BuildChannelColumn();
        _traceView = new ScrollViewerView(TraceScroll);
        _edgeScroll = new EdgeScroll(_traceView, EdgeScroll.UnzoomedRollColumn);
        HookTrace();
        _traceFollower = new PlayheadFollower(TraceScroll);

        _readoutTimer = new DispatcherTimer { Interval = ReadoutInterval };
        _readoutTimer.Tick += (_, _) => TakeReadout();
        _playheadTimer = PlayheadTimer.Build();
        _playheadTimer.Tick += (_, _) => FollowPlayback();

        foreach (var value in SongPanel.GetLogicalDescendants().OfType<SelectableTextBlock>())
        {
            CutOffTextTips.CutOffWithTip(value);
        }

        ListColumn.Add(SpcTab.Song, SongPanel);
        ListColumn.Add(SpcTab.Voices, VoicePanel);
        ListColumn.Add(SpcTab.Samples, SamplePanel);
        ListColumn.Select(SpcTab.Song);
        ListColumn.StandLevelWithTopOf(TraceFrame);
        ListDivider.DriveSizeOf(() => ListColumn.Width, ApplyListColumnWidth);
        ListColumn.CornerPressed += () => PopOutSidePanel(null);
        ShowListColumn(false);

        Closed += (_, _) =>
        {
            _closing.Cancel();
            ReleaseEngine();
            _sidePanelWindow?.CloseWithoutDocking();
        };
    }

    // Builds the window with the canvas's view following the playhead whenever the main window's Follow Playback is on, and the side panel standing
    // where it was left.
    public SpcPlayerWindow(Func<bool> isFollowPlaybackOn, PanelPlacement sidePanelPlacement) : this()
    {
        _isFollowPlaybackOn = isFollowPlaybackOn;
        _sidePanelPlacementToRestore = sidePanelPlacement.PoppedOut ? sidePanelPlacement : null;
    }

    // Raised by a sample row's '+': one .brr file's bytes, header and all.
    public event Action<byte[]>? SampleExtracted;

    // Raised by a voice row's '+': the voice's .brr file bytes and the envelope registers it is sounding under.
    public event Action<byte[], EnvelopeState>? InstrumentExtracted;

    // Raised by the two transcribe buttons: the open dump, what playing it through heard and the frame to transcribe it up to.
    public event Action<SpcFile, SpcTrace, long>? TranscribeRequested;

    // Where the playhead is drawn: its position folded back into the loop.
    private long FoldedPlayheadFrame
    {
        get
        {
            return Trace.FoldedIntoLoop(Trace.Position);
        }
    }

    // How long the open trace runs in frames, or none where nothing is open.
    private long TraceLengthFrames
    {
        get
        {
            return _trace?.LengthFrames ?? 0;
        }
    }

    // Picks an .spc, reads it, plays it through behind a progress box and readies it to play, leaving whatever was open in place where the file cannot be
    // read or played or the box is cancelled.
    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (await SpcFileOpening.PickAndRead(this, WindowTitles.OpenSpc) is not var (chosenPath, file))
        {
            return;
        }

        SpcEngine? engine = null;
        SpcEngine? auditionEngine = null;
        AudioEngine sampleAuditionAudio;
        try
        {
            engine = new SpcEngine(new MiniAudioOutput(), file.FileBytes);
            auditionEngine = new SpcEngine(new MiniAudioOutput(), file.FileBytes);
            sampleAuditionAudio = new AudioEngine(new MiniAudioOutput());
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
        {
            engine?.Dispose();
            auditionEngine?.Dispose();
            await MessageDialog.Show(this, ex.Message, WindowTitles.Failures.FailedToPlaySpc);
            return;
        }

        var run = await SpcTraceRun.BehindProgress(this, WindowTitles.OpenSpc, LoadingLine, file, _closing.Token);
        if (_closing.IsCancellationRequested || run.IsCancelled)
        {
            engine.Dispose();
            auditionEngine.Dispose();
            sampleAuditionAudio.Dispose();
            return;
        }

        ShowOpenedFile(chosenPath, file, engine, auditionEngine, sampleAuditionAudio, run.Value);
        if (_trace is { HasNoLoopOrEnd: true })
        {
            await MessageDialog.Show(this, QuestionsAndNotices.NoLoopOrEndFound(SpcTrace.MaxCaptureMinutes), WindowTitles.Notices.NoLoopFound);
        }
    }

    // Puts a file that has been played through in place of whatever was open: its emulators and the sample auditions' device, its name and tags, its
    // trace on the canvas, and the lists, the mutes and the view as they stand at its start, with the side panel shown.
    private void ShowOpenedFile(string chosenPath, SpcFile file, SpcEngine engine, SpcEngine auditionEngine, AudioEngine sampleAuditionAudio, SpcTrace? trace)
    {
        ReleaseEngine();
        _engine = engine;
        _auditionEngine = auditionEngine;
        _sampleAuditionAudio = sampleAuditionAudio;
        _openFile = file;
        _trace = trace;
        Title = WindowTitles.WithDocument(WindowTitles.SpcPlayer, file.Title.Length > 0 ? file.Title : Path.GetFileNameWithoutExtension(chosenPath));
        ShowTags(file.Title, file.Artist, file.Game, file.Comment);
        Transport.ResetPlayback();
        Transport.Buttons.IsEnabled = true;
        Trace.SetTrace(_trace, TraceScroll.Viewport.Width);
        _textBrushesOfSrcn.Clear();
        TranscribeButton.IsEnabled = _trace is not null;
        ForEachEngine(openedEngine => openedEngine.SetCheckpoints(_trace?.Checkpoints));
        ResetListsAndView();
        ShowSidePanel();
    }

    // Forgets what was picked, muted and shown alone, and jumps the playhead to the start.
    private void ResetListsAndView()
    {
        ForgetSelection();
        _voiceMuting.Clear();
        _sampleMuting.Clear();
        ApplySampleMutes();
        ShowVoiceAlone(null);
        ShowStoppedAtStart();
    }

    // Shows the four tags in the song tab.
    private void ShowTags(string title, string artist, string game, string comment)
    {
        TitleText.Text = title;
        AuthorText.Text = artist;
        GameText.Text = game;
        CommentText.Text = comment;
    }

    // Writes the chip's master volume and its echo into the song tab's rows under the tags.
    private void ShowChipSettings(SpcTraceMasterVolume masterVolume, SpcTraceEcho echo)
    {
        MasterVolumeText.Text = VolumeText(masterVolume.Left, masterVolume.Right);
        EchoVolumeText.Text = VolumeText(SnesEcho.SignedValueOf(echo.VolumeLeft), SnesEcho.SignedValueOf(echo.VolumeRight));
        EchoFeedbackText.Text = SnesEcho.SignedValueOf(echo.Feedback).ToString();
        EchoDelayText.Text = echo.Delay.ToString();
        EchoFilterText.Text = AmkCommands.HexSpelling([.. SnesEcho.UnpackCoefficients(echo.Coefficients).Select(coefficient => (int)(byte)coefficient)]);
        EchoWritesText.Text = echo.IsEnabled ? EchoWritesOnText : EchoWritesOffText;
    }

    // Empties the song tab's master volume and echo rows, which is what they show with no file open.
    private void ClearChipSettings()
    {
        foreach (var text in (SelectableTextBlock[])[MasterVolumeText, EchoVolumeText, EchoFeedbackText, EchoDelayText, EchoFilterText, EchoWritesText])
        {
            text.Text = "";
        }
    }

    // Hands the open dump and all of its trace to the main window.
    private void OnTranscribeClick(object? sender, RoutedEventArgs e)
    {
        RequestTranscriptionEndingAt(TraceLengthFrames);
    }

    // Hands the open dump and its trace up to the playhead to the main window.
    private void OnTranscribeToMarkerClick(object? sender, RoutedEventArgs e)
    {
        RequestTranscriptionEndingAt(FoldedPlayheadFrame);
    }

    // Hands the open dump and its trace to the main window to transcribe up to a frame, a frame at or past the trace's end transcribing all of it.
    private void RequestTranscriptionEndingAt(long frame)
    {
        if (_openFile is null || _trace is null)
        {
            return;
        }

        TranscribeRequested?.Invoke(_openFile, _trace, frame);
    }

    // Enables the button that transcribes up to the playhead where the whole song can be transcribed and the playhead stands past the start.
    private void EnableTranscribeToMarker()
    {
        TranscribeToMarkerButton.IsEnabled = TranscribeButton.IsEnabled && FoldedPlayheadFrame > 0;
    }

    // Plays, pauses or resumes.
    private void OnPlayPauseClick(object? sender, RoutedEventArgs e)
    {
        TogglePlayback();
    }

    // Plays, pauses or resumes, a pause taking one last readout at the instant the chip stopped.
    private void TogglePlayback()
    {
        if (_engine is null)
        {
            return;
        }

        if (_engine.IsPlaying)
        {
            _engine.Pause();
            StopTimers();
            TakeReadout();
            Transport.Buttons.ShowPlay();
            return;
        }

        _engine.Play();
        _readoutTimer.Start();
        _playheadTimer.Start();
        Transport.Buttons.ShowPause();
    }

    // Stops and goes back to the song's start.
    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        if (_engine is null)
        {
            return;
        }

        _engine.Stop();
        StopTimers();
        ShowStoppedAtStart();
    }

    // Shows the transport stopped, with the play button up and the playhead at the start.
    private void ShowStoppedAtStart()
    {
        Transport.Buttons.ShowPlay();
        JumpPlayheadTo(0);
    }

    // Stops the readout's timer and the playhead's.
    private void StopTimers()
    {
        _readoutTimer.Stop();
        _playheadTimer.Stop();
    }

    // Stops the timers and lets go of the emulators playing the open file and its voice auditions and of the device playing its sample auditions.
    private void ReleaseEngine()
    {
        StopTimers();
        ForEachEngine(engine => engine.Dispose());
        _sampleAuditionAudio?.Dispose();
        _engine = null;
        _auditionEngine = null;
        _sampleAuditionAudio = null;
    }

    // Runs an action on the emulator playing the open file and on the one playing its auditions, where they are open.
    private void ForEachEngine(Action<SpcEngine> action)
    {
        foreach (var engine in (SpcEngine?[])[_engine, _auditionEngine])
        {
            if (engine is not null)
            {
                action(engine);
            }
        }
    }

    // Forgets which list entry and which voice are selected.
    private void ForgetSelection()
    {
        _selectedEntryIndex = null;
        _selectedVoice = null;
    }

    // Puts the chip at a frame and reads it out there, carrying on from it where playing and standing on it where paused.
    private void SeekTo(long frame)
    {
        if (_engine is null)
        {
            return;
        }

        _engine.Seek(frame);
        JumpPlayheadTo(frame);
    }

    // Moves the canvas's playhead to what is being heard, unless it is being dragged, and scrolls the view after it where Follow Playback is on.
    private void FollowPlayback()
    {
        if (_engine is null || Trace.IsDraggingPlayhead)
        {
            return;
        }

        MovePlayheadTo(_engine.PositionFrames);
        if (_isFollowPlaybackOn() && !Trace.IsPanning && Trace.PlayheadX is double playheadX)
        {
            _traceFollower.KeepInView(playheadX);
        }
    }

    // Puts the canvas's playhead at a frame and the timer, the position slider and the button transcribing up to it with it.
    private void MovePlayheadTo(long frame)
    {
        Trace.Position = frame;
        ShowWhatFollowsPlayhead();
    }

    // Puts the playhead at a frame the chip has jumped to and reads the chip out there, nothing between the last readout and the jump having played.
    private void JumpPlayheadTo(long frame)
    {
        MovePlayheadTo(frame);
        _lastReadoutPosition = null;
        TakeReadout();
    }

    // Writes the timer and the position slider and enables the button transcribing up to the playhead, all from where the playhead stands.
    private void ShowWhatFollowsPlayhead()
    {
        ShowPlaybackTime();
        EnableTranscribeToMarker();
    }

    // Hands the three playback sliders to the emulator and the position slider to the seek, and lines the bar's ends up with the canvas and the lists.
    private void HookTransport()
    {
        Transport.Buttons.IsEnabled = false;
        Transport.PlaybackChanged += () => ForEachEngine(engine => engine.SetPlayback(Transport.VolumeGain, Transport.SpeedMultiplier, Transport.TransposeSemitones));
        Transport.PositionMoved += frame => SeekTo((long)Math.Round(frame));
        LayoutUpdated += (_, _) => Transport.AlignEnds(TraceFrame, ListColumn.IsVisible ? ListColumn : null);
        ShowPlaybackTime();
    }

    // Shows or hides the list column and the divider beside it, the canvas taking the room while they are hidden.
    private void ShowListColumn(bool isShown)
    {
        ListColumn.IsVisible = isShown;
        ListDivider.IsVisible = isShown;
    }

    // Lays the list column out at a width, held between the narrowest it may be dragged and the width it opens at.
    private void ApplyListColumnWidth(double width)
    {
        ListColumn.Width = Math.Clamp(width, ThemeResource.Of<double>(ListColumnMinWidthResource), ThemeResource.Of<double>(ListColumnWidthResource));
    }

    // Shows the side panel once a file has been read through: in a window of its own where one stands open or was left open last time, and in the
    // list column otherwise.
    private void ShowSidePanel()
    {
        if (_sidePanelWindow is not null)
        {
            return;
        }

        if (_sidePanelPlacementToRestore is { } placement)
        {
            _sidePanelPlacementToRestore = null;
            PopOutSidePanel(placement);
            return;
        }

        ShowListColumn(true);
    }

    // Takes the side panel's tabs out of the list column into a window of their own, standing where a placement says or centered on this window.
    private void PopOutSidePanel(PanelPlacement? placement)
    {
        var deck = new SpcDeck { DoesCornerDock = true };
        deck.CornerPressed += DockSidePanel;
        double deckHeight = ListColumn.Bounds.Height;
        ListColumn.MoveAllInto(deck);
        ShowListColumn(false);

        var window = PanelWindow.Create(WindowTitles.SpcPlayerPanels, placement, deckHeight, ListColumn.Width, DockSidePanel);
        window.MinWidth = ThemeResource.Of<double>(ListColumnMinWidthResource);
        window.KeyPressed += e => HandleKey(e, window.FocusManager?.GetFocusedElement());
        window.Take(deck);
        _sidePanelWindowDeck = deck;
        _sidePanelWindow = window;
        window.ShowAt(placement, this);
    }

    // Puts the side panel's tabs back in the list column, showing the tab the window was showing, and closes the window.
    private void DockSidePanel()
    {
        if (_sidePanelWindow is not { } window)
        {
            return;
        }

        _sidePanelWindow = null;
        _sidePanelWindowDeck!.MoveAllInto(ListColumn);
        _sidePanelWindowDeck = null;
        window.Release();
        ShowListColumn(true);
        window.CloseUnlessClosingItself();
    }

    // Where the side panel stands, to be put back there the next time the window opens: its own window's placement, the one still waiting for a file
    // to be read through, or the list column's.
    public PanelPlacement CaptureSidePanelPlacement()
    {
        return _sidePanelWindow?.CapturePlacement() ?? _sidePanelPlacementToRestore ?? new PanelPlacement();
    }

    // Writes the timer and the position slider: where the playhead is, folded back into the loop, and how long the traced song runs.
    private void ShowPlaybackTime()
    {
        long lengthFrames = TraceLengthFrames;
        long atFrame = FoldedPlayheadFrame;
        Transport.ShowPosition(atFrame, Math.Max(lengthFrames, MinPositionRailFrames), atFrame / DspPoint.SampleRate, lengthFrames / DspPoint.SampleRate);
    }

    // Hooks the canvas: a press seeks, a block picks its voice, a block and the ruler sound a voice under them while paused, a playhead dragged past either
    // edge scrolls it, a bare notch scrolls it sideways, and the pitch ruler reads it.
    private void HookTrace()
    {
        PitchRuler.Trace = Trace;
        TraceMarkers.ShowOver(Trace, Trace, TraceScroll);
        Trace.PositionRequested += SeekTo;
        Trace.VoicePicked += voice =>
        {
            SelectVoice(voice);
            if (!IsPlaying())
            {
                TryStartAudition(voice);
            }
        };
        Trace.RulerPressed += frame =>
        {
            if (!IsPlaying())
            {
                AuditionShownVoiceAt(frame);
            }
        };
        Trace.PressEnded += ReleaseAudition;
        Trace.DragAt += x => _edgeScroll.FollowDrag(x, null, Trace.FollowCursor);
        Trace.PressEnded += _edgeScroll.Stop;
        Trace.ScrollRequested += offset => TraceScroll.Offset = TraceScroll.Offset.WithX(offset);
        TraceScroll.SizeChanged += (_, _) => Trace.ClampZoom();
        TraceScroll.AddHandler(PointerWheelChangedEvent, OnTraceScrollWheel, RoutingStrategies.Tunnel);
    }

    // Scrolls the canvas sideways on a bare notch, as Shift and the wheel do.
    private void OnTraceScrollWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        ScrollPans.PassNotchTo(TraceScroll, e, KeyModifiers.Shift);
    }

    // Reads the chip as it stands and writes both lists.
    private void TakeReadout()
    {
        var state = _shownState = CurrentState();
        if (state is null)
        {
            ShowDirectory([]);
            ClearVoiceRows();
            ClearChipSettings();
            return;
        }

        ShowDirectory(SpcSampleDirectory.EntriesOf(state, _trace?.SampleSrcns ?? []));
        RecordSoundingEntries();

        var registers = state.DspRegisters;
        var echo = SpcTraceEcho.Of(registers, FoldedPlayheadFrame);
        ShowChipSettings(SpcTraceMasterVolume.Of(registers, FoldedPlayheadFrame), echo);
        for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
        {
            int entryIndex = SnesDspRegister.SourceOf(registers, voice);
            var envelope = SnesDspRegister.EnvelopeOf(registers, voice);
            int envelopeLevel = SnesDspRegister.EnvelopeLevelOf(registers, voice);
            int pitchRegister = SnesDspRegister.PitchOf(registers, voice);
            var volume = SnesDspRegister.VolumeOf(registers, voice);
            bool isSounding = envelopeLevel > 0;
            bool isPlayingNoise = SnesDspRegister.IsPlayingNoise(registers, voice);
            var cells = _voiceCells[voice];
            VoiceTable.WriteRow(
                cells,
                (voice + 1).ToString(),
                RowListing(entryIndex) is int sampleRow ? SampleNumberText(sampleRow) : ValueTexts.NoValue,
                AdsrText(envelope.Adsr1, envelope.Adsr2),
                AmkCommands.HexSpelling(envelope.Gain),
                PitchText(pitchRegister),
                envelopeLevel.ToString(),
                VolumeText(volume.Left, volume.Right));
            ListTable.ShowCheck(cells[VoiceEchoColumn], SnesDspRegister.HasVoiceBit(echo.Sends, voice));
            ListTable.ShowCheck(cells[VoiceNoiseColumn], isPlayingNoise);
            _voiceEntryIndexes[voice] = entryIndex;
            PaintCells(cells, isSounding);
        }

        PaintRows();
    }

    // Notes the entries not silenced that a voice not silenced played as a sample from the last readout's playhead to this one's, and keeps this one's
    // playhead.
    private void RecordSoundingEntries()
    {
        long toPosition = Trace.Position;
        long fromPosition = Math.Min(_lastReadoutPosition ?? toPosition, toPosition);

        // The blocks between the two positions that a voice not silenced played as a sample not silenced.
        var soundingBlocks = Trace.BlocksSoundingBetweenPositions(fromPosition, toPosition)
            .Where(block => !block.IsNoise && !_voiceMuting.IsSilenced(block.Voice) && !_sampleMuting.IsSilenced(block.Srcn));
        _soundingEntryIndexes.Clear();
        _soundingEntryIndexes.UnionWith(soundingBlocks.Select(block => block.Srcn));
        _lastReadoutPosition = toPosition;
    }

    // The chip as it stands: the emulator's state where one is loaded, and the opened file where none is or its snapshot does not parse.
    private SpcFile? CurrentState()
    {
        byte[]? snapshot = _engine?.Snapshot();
        if (snapshot is null)
        {
            return _openFile;
        }

        try
        {
            return SpcReader.Parse(snapshot);
        }
        catch (SpcReadException)
        {
            return _openFile;
        }
    }

    // Empties every voice row but its number.
    private void ClearVoiceRows()
    {
        for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
        {
            var cells = _voiceCells[voice];
            foreach (var cell in cells)
            {
                ListTable.ClearCell(cell);
            }

            VoiceTable.WriteRow(cells, (voice + 1).ToString());
            _voiceEntryIndexes[voice] = NoEntry;
            PaintCells(cells, false);
        }

        PaintRows();
    }

    // Builds the eight voice rows once, each with a '+' and a play button docked at its right that show while the pointer is over the row or it is selected,
    // the '+' adding its own voice to the song as an instrument and the play button sounding its own voice while held.
    private void BuildVoiceRows()
    {
        for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
        {
            int rowVoice = voice;
            var buttonSlot = BuildAddAndPlayButtons(() => AddInstrumentFrom(rowVoice), Tooltips.AddInstrumentToSong, () => TryStartAudition(rowVoice), ReleaseAudition,
                Tooltips.PlayVoice);
            var cells = new TextBlock[VoiceListColumns.Length];
            var row = VoiceTable.BuildRow(cells, buttonSlot);
            row.PointerPressed += (_, _) => ToggleVoiceSelection(rowVoice);

            _voiceRows[voice] = row;
            _voiceCells[voice] = cells;
            _voiceButtonSlots[voice] = buttonSlot;
            VoiceTable.Rows.Add(row);
        }

        ClearVoiceRows();
    }

    // Shows the entries the last readout found for the Samples table, numbered in order, each row with a mute and a solo toggle, a '+' and a play button
    // docked at its right, the '+' adding its own entry to the song as a sample and the play button sounding it while held, building the rows again only
    // where they changed.
    private void ShowDirectory(IReadOnlyList<SpcDirectoryEntry> entries)
    {
        if (entries.SequenceEqual(_directoryEntries))
        {
            return;
        }

        _directoryEntries = entries;
        if (_selectedEntryIndex is int selectedIndex && !entries.Any(entry => entry.Index == selectedIndex))
        {
            ForgetSelection();
        }

        SampleTable.Rows.Clear();
        _sampleRows.Clear();
        _sampleCells.Clear();
        _sampleToggles.Clear();
        Trace.SampleShownInColor = null;
        for (int rowIndex = 0; rowIndex < entries.Count; rowIndex++)
        {
            var entry = entries[rowIndex];
            int entryIndex = entry.Index;
            var buttons = BuildAddAndPlayButtons(() => AddSample(entryIndex), Tooltips.AddSampleToSong, () => StartSampleAudition(entryIndex), ReleaseSampleAudition,
                Tooltips.PlaySample);

            var toggles = new MuteSoloPair(entryIndex, Tooltips.MuteSample, Tooltips.PlaySampleSolo, OnSampleMuteToggled, OnSampleSoloToggled);
            var cells = new TextBlock[SampleListColumns.Length];
            var row = SampleTable.BuildRow(cells, toggles.Panel, buttons);
            row.Classes.Add(ThemeClasses.ButtonPlacesKept);
            SampleTable.WriteRow(cells, SampleNumberText(rowIndex), entry.BlockCount.ToString());
            ListTable.ShowCheck(cells[SampleLoopColumn], entry.DoesLoop);
            row.PointerPressed += (_, _) => ToggleEntrySelection(entryIndex);
            row.PointerEntered += (_, _) => Trace.SampleShownInColor = entryIndex;
            row.PointerExited += (_, _) => Trace.SampleShownInColor = null;

            _sampleRows.Add(row);
            _sampleCells.Add(cells);
            _sampleToggles.Add(toggles);
            SampleTable.Rows.Add(row);
        }
    }

    // A row's '+' and play button side by side, the '+' running an add on its click and the play button running an audition for as long as it is held.
    private static StackPanel BuildAddAndPlayButtons(Action add, string addTooltip, Action startAudition, Action releaseAudition, string playTooltip)
    {
        var addButton = RowIconButton.Build(AppIcons.Plus);
        addButton.Click += (_, _) => add();
        ToolTip.SetTip(addButton, addTooltip);
        var playButton = RowIconButton.Build(AppIcons.Play);
        HeldPress.HookPressAndRelease(playButton, startAudition, releaseAudition);
        ToolTip.SetTip(playButton, playTooltip);

        return new StackPanel { Orientation = Orientation.Horizontal, Children = { addButton, playButton } };
    }

    // Picks a directory entry from the sample list and no voice, or drops it where it was picked already.
    private void ToggleEntrySelection(int entryIndex)
    {
        // Whether the entry is picked already from the sample list, which a second press drops.
        bool isDropped = _selectedEntryIndex == entryIndex && _selectedVoice is null;
        _selectedEntryIndex = isDropped ? null : entryIndex;
        _selectedVoice = null;
        PaintRows();
    }

    // Picks a voice from its row and the entry it plays with it, or drops both where it was picked already, where it plays an entry.
    private void ToggleVoiceSelection(int voice)
    {
        if (_voiceEntryIndexes[voice] == NoEntry)
        {
            return;
        }

        if (_selectedVoice == voice)
        {
            ForgetSelection();
            PaintRows();
            return;
        }

        SelectVoice(voice);
    }

    // Picks a voice and the entry it plays, where it plays one.
    private void SelectVoice(int voice)
    {
        if (_voiceEntryIndexes[voice] == NoEntry)
        {
            return;
        }

        _selectedVoice = voice;
        _selectedEntryIndex = _voiceEntryIndexes[voice];
        PaintRows();
    }

    // Paints what both lists say beyond the registers - a sample heard, its mute and solo and the rows the picked entry lights - and shows a voice row's
    // buttons only where it plays an entry.
    private void PaintRows()
    {
        for (int row = 0; row < _sampleRows.Count; row++)
        {
            int entryIndex = _directoryEntries[row].Index;
            var (brightBrush, dullBrush) = TextBrushesOfSample(entryIndex);
            PaintCells(_sampleCells[row], _soundingEntryIndexes.Contains(entryIndex), brightBrush, dullBrush);
            _sampleToggles[row].ShowState(_sampleMuting.IsMuted(entryIndex), _sampleMuting.IsSoloed(entryIndex), ThemeResource.TextPrimaryBrush);
            ListRows.ShowSelected(_sampleRows[row], _selectedEntryIndex == entryIndex);
        }

        for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
        {
            ListRows.ShowSelected(_voiceRows[voice], _selectedEntryIndex is int selectedIndex && _voiceEntryIndexes[voice] == selectedIndex);
            _voiceButtonSlots[voice].IsVisible = EntryPlayedBy(voice) is not null;
        }
    }

    // The picked entry, or null where nothing is picked or the Samples table no longer lists it.
    private SpcDirectoryEntry? SelectedEntry()
    {
        return _selectedEntryIndex is int entryIndex ? ListedEntry(entryIndex) : null;
    }

    // The entry a voice is playing, or null where it has nothing loaded or the Samples table does not list what it names.
    private SpcDirectoryEntry? EntryPlayedBy(int voice)
    {
        int entryIndex = _voiceEntryIndexes[voice];
        return entryIndex == NoEntry ? null : ListedEntry(entryIndex);
    }

    // A directory entry as the Samples table lists it, or null where the table does not list it.
    private SpcDirectoryEntry? ListedEntry(int entryIndex)
    {
        return RowListing(entryIndex) is int row ? _directoryEntries[row] : null;
    }

    // The row of the Samples table listing a directory entry, or null where the table does not list it.
    private int? RowListing(int entryIndex)
    {
        for (int row = 0; row < _directoryEntries.Count; row++)
        {
            if (_directoryEntries[row].Index == entryIndex)
            {
                return row;
            }
        }

        return null;
    }

    // The number a row of the Samples table shows, which the voice list's Sample column shows for the sample too.
    private static string SampleNumberText(int row)
    {
        return (row + 1).ToString();
    }

    // Builds the stacked-view button and the eight channel buttons under it.
    private void BuildChannelColumn()
    {
        _stackedButton = ChannelButton.CreateLabeled("Stacked", Palette.SilverGray, () => ShowVoiceAlone(null));
        StackedSlot.Children.Add(_stackedButton.Button);

        for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
        {
            _channelButtons[voice] = ChannelButton.Create(voice, Tooltips.MuteVoice, Tooltips.PlayVoiceSolo, OnVoiceMuteToggled, OnVoiceSoloToggled, pressedVoice => ShowVoiceAlone(pressedVoice));
            ChannelButtons.Children.Add(_channelButtons[voice].Button);
        }

        PaintChannelButtons();
    }

    // Puts one voice across the canvas's whole height with its pitch scale beside it, or all eight stacked where none is given.
    private void ShowVoiceAlone(int? voice)
    {
        Trace.VoiceShownAlone = voice;
        PitchRuler.IsVisible = voice is not null;
        PitchRulerLine.IsVisible = voice is not null;
        PaintChannelButtons();
    }

    // Mutes a voice or unmutes it, from its mute toggle.
    private void OnVoiceMuteToggled(int voice, bool isMuted)
    {
        _voiceMuting.SetMuted(voice, isMuted);
        ApplyVoiceMutes();
    }

    // Solos a voice or takes its solo off, from its solo toggle.
    private void OnVoiceSoloToggled(int voice, bool isSoloed)
    {
        _voiceMuting.SetSoloed(voice, isSoloed);
        ApplyVoiceMutes();
    }

    // Hands the emulator the mask of voices the toggles silence and repaints them, taking a readout while paused to show the voices keyed off.
    private void ApplyVoiceMutes()
    {
        int silencedMask = 0;
        for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
        {
            if (_voiceMuting.IsSilenced(voice))
            {
                silencedMask |= SnesDspRegister.VoiceBit(voice);
            }
        }

        _engine?.MuteVoices(silencedMask);
        PaintChannelButtons();
        TakeReadoutWhilePaused();
    }

    // Mutes a sample or unmutes it, from its mute toggle.
    private void OnSampleMuteToggled(int entryIndex, bool isMuted)
    {
        _sampleMuting.SetMuted(entryIndex, isMuted);
        ApplySampleMutes();
    }

    // Solos a sample or takes its solo off, from its solo toggle.
    private void OnSampleSoloToggled(int entryIndex, bool isSoloed)
    {
        _sampleMuting.SetSoloed(entryIndex, isSoloed);
        ApplySampleMutes();
    }

    // Hands the emulator the schedule of voices the samples' toggles silence, grays those samples on the canvas and repaints the toggles, taking a
    // readout while paused to show the voices keyed off.
    private void ApplySampleMutes()
    {
        Trace.SilencedSrcns = Enumerable.Range(0, SpcSampleDirectory.MaxEntries).Where(_sampleMuting.IsSilenced).ToHashSet();
        _engine?.SetSampleMuteSchedule(_trace is null ? null : new SpcSampleMuteSchedule(_trace, _sampleMuting.IsSilenced));
        PaintRows();
        TakeReadoutWhilePaused();
    }

    // Takes a readout while the song is not being heard.
    private void TakeReadoutWhilePaused()
    {
        if (!IsPlaying())
        {
            TakeReadout();
        }
    }

    // Whether the open file is being heard.
    private bool IsPlaying()
    {
        return _engine?.IsPlaying == true;
    }

    // Draws each button filled where it is the view showing, and its toggles saying the voice's mute and solo.
    private void PaintChannelButtons()
    {
        _stackedButton.ShowState(Trace.VoiceShownAlone is null);
        for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
        {
            _channelButtons[voice].ShowState(Trace.VoiceShownAlone == voice, _voiceMuting.IsMuted(voice), _voiceMuting.IsSoloed(voice));
        }
    }

    // Sounds a voice's note under the marker from its key-on to its end, alone and as the chip plays it, through the audition emulator.
    private void TryStartAudition(int voice)
    {
        if (_auditionEngine is null || NoteUnderMarker(voice) is not { } note)
        {
            return;
        }

        _auditionEngine.PlayVoiceAlone(voice, note.Start, note.End);
    }

    // Silences the audition, which is what letting go of the press that started it does.
    private void ReleaseAudition()
    {
        _auditionEngine?.MuteVoices(SnesDspRegister.AllVoicesMask);
    }

    // Sounds the voice shown alone where a block of it is under a press on the seconds ruler; with the voices stacked a press there sounds nothing.
    private void AuditionShownVoiceAt(long frame)
    {
        if (Trace.VoiceShownAlone is int shownVoice && Trace.VoicesSoundingAt(frame).Contains(shownVoice))
        {
            TryStartAudition(shownVoice);
        }
    }

    // The note a voice last keyed on at or before the marker, or null where it has keyed on none yet or nothing has been traced.
    private SpcTraceBlock? NoteUnderMarker(int voice)
    {
        return _trace?.LastBlockKeyedOn(voice, FoldedPlayheadFrame);
    }

    // The sound a voice is making in the state last read out, under the envelope its note keyed on with, or null where it plays nothing the directory holds.
    private VoiceSound? SoundOf(int voice)
    {
        if (_shownState is not { } state || EntryPlayedBy(voice) is not { } entry)
        {
            return null;
        }

        // The envelope the voice's note keyed on with, or the one its registers hold now where no note is under the marker.
        var envelope = NoteUnderMarker(voice)?.KeyOnEnvelope ?? SnesDspRegister.EnvelopeOf(state.DspRegisters, voice);
        return new VoiceSound(SpcExtract.BrrFileBytes(state.Aram, entry), envelope);
    }

    // Sounds a directory entry's sample alone as the last readout found it, whole from its start and looping where it loops, at the rate the Sample Editor
    // first plays a sample at, where the Samples table lists it; bytes that are not BRR sound nothing.
    private void StartSampleAudition(int entryIndex)
    {
        if (_sampleAuditionAudio is null || ListedBrrFileBytes(entryIndex) is not { } brrFileBytes)
        {
            return;
        }

        try
        {
            _sampleAuditionAudio.Mixer.Preview.StartWhole(Sample.FromBytes(BrrSample.DefaultName, brrFileBytes));
        }
        catch (InvalidDataException)
        {
        }
    }

    // Silences a sample row's audition, which is what letting go of the press that started it does.
    private void ReleaseSampleAudition()
    {
        _sampleAuditionAudio?.Mixer.Preview.Stop();
    }

    // Hands over a directory entry's .brr file bytes as the last readout found them, where the Samples table lists it.
    private void AddSample(int entryIndex)
    {
        if (ListedBrrFileBytes(entryIndex) is not { } brrFileBytes)
        {
            return;
        }

        SampleExtracted?.Invoke(brrFileBytes);
    }

    // A directory entry's .brr file bytes as the last readout found them, or null where nothing has been read out or the Samples table does not list it.
    private byte[]? ListedBrrFileBytes(int entryIndex)
    {
        if (_shownState is not { } state || ListedEntry(entryIndex) is not { } entry)
        {
            return null;
        }

        return SpcExtract.BrrFileBytes(state.Aram, entry);
    }

    // Hands over a voice's sample and the envelope registers it is sounding under, as the last readout found them.
    private void AddInstrumentFrom(int voice)
    {
        if (SoundOf(voice) is not { } sound)
        {
            return;
        }

        InstrumentExtracted?.Invoke(sound.BrrFileBytes, sound.Envelope);
    }

    // A voice's ADSR1 and ADSR2 as one hex word, ADSR1 first.
    private static string AdsrText(int adsr1, int adsr2)
    {
        return $"${adsr1:X2}{adsr2:X2}";
    }

    // A voice's PITCH as a hex word.
    private static string PitchText(int pitchRegister)
    {
        return $"${pitchRegister:X4}";
    }

    // A left and a right volume side by side, signed.
    private static string VolumeText(int left, int right)
    {
        return $"{left}{VolumeSidesSeparator}{right}";
    }

    // Draws a row's text in the plain text colors, bright where the chip is making a sound with it and dull where it is not.
    private static void PaintCells(TextBlock[] cells, bool isLive)
    {
        PaintCells(cells, isLive, ThemeResource.TextPrimaryBrush, ThemeResource.TextDisabledBrush);
    }

    // Draws a row's text in its bright brush where the chip is making a sound with it and in its dull one where it is not.
    private static void PaintCells(TextBlock[] cells, bool isLive, IBrush brightBrush, IBrush dullBrush)
    {
        var brush = isLive ? brightBrush : dullBrush;
        foreach (var cell in cells)
        {
            cell.Foreground = brush;
        }
    }

    // A sample's text brushes in the Samples table: its color on the canvas, and that color dulled as the disabled text is.
    private (IBrush Bright, IBrush Dull) TextBrushesOfSample(int srcn)
    {
        if (_textBrushesOfSrcn.TryGetValue(srcn, out var brushes))
        {
            return brushes;
        }

        var color = Trace.ColorOfSample(srcn);
        brushes = (new SolidColorBrush(color), new SolidColorBrush(Palette.AsDisabledText(color)));
        _textBrushesOfSrcn[srcn] = brushes;
        return brushes;
    }

    // Reads a key going down in this window, ahead of whatever has the focus.
    protected override void HandleWindowKeyDown(KeyEventArgs e)
    {
        HandleKey(e, FocusManager?.GetFocusedElement());
    }

    // Plays or pauses on a bare space, and pans the canvas to its start or end on Home or End - putting playback there with Shift - in this window or in
    // the side panel's own.
    private void HandleKey(KeyEventArgs e, IInputElement? focused)
    {
        if (e.KeyModifiers == KeyModifiers.None && WindowKeys.IsPlayKey(e, focused))
        {
            TogglePlayback();
            e.Handled = true;
            return;
        }

        if (ViewEdges.TryPanToEdgeOfKey(_traceView, e, EdgeActionOf))
        {
            e.Handled = true;
        }
    }

    // What Home or End does at its edge with modifiers held: Shift puts playback there.
    private Action<ViewEdge>? EdgeActionOf(KeyModifiers modifiers)
    {
        return modifiers == KeyModifiers.Shift ? edge => SeekTo(ViewEdges.ValueAt(edge, 0, TraceLengthFrames)) : null;
    }

    // What a voice is sounding: its sample as a .brr file's bytes and the envelope registers it sounds under.
    private sealed record VoiceSound(byte[] BrrFileBytes, EnvelopeState Envelope);
}
