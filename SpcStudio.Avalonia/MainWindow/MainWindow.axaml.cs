using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Export;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio;

// The app's main window: the roll, its lanes, the panel column and the transport, over the one song that is open.
public partial class MainWindow : AppWindow
{
    private const string WarmUpDropDownItem = "0";

    private readonly DispatcherTimer _playheadTimer = PlayheadTimer.Build();
    private readonly WindowPlacement _placement;
    private readonly ScrollViewerView _rollView;
    private readonly EdgeScroll _edgeScroll;
    private readonly LaneRow[] _laneRows;
    private readonly ChangeLane[] _lanes;
    private readonly EditHistory _history;
    private readonly SampleHistory _sampleHistory;
    private readonly InstrumentLibraryHistory _pickerHistory = new();
    private Song _song = NewEmptySong();
    private AudioEngine? _audio;
    private int? _currentInstrumentId;
    private bool _isUpdatingNote;
    private ChangeLane? _cellLane;
    private int _cellTick;
    private bool _doesCellTakeFocus = true;
    private bool _isKeepingCell;
    private int? _cellDragTick;
    private InstrumentPickerWindow? _picker;
    private HashSet<string> _pickerOpenFolders = [];
    private SampleEditorWindow? _sampleEditor;
    private string? _songPath;
    private string? _mmlPath;
    private bool _doesOpenMmlAfterExport = true;
    private bool _doesRomExtractionIncludeSmwOriginals;
    private bool _doesCheckForUpdatesAtLaunch = true;
    private MmlExportOptions _exportOptions = MmlExportOptions.All;
    private string? _startupSongPath;
    private double _playStart;
    private bool _isChannelStepKeyDown;

    // Moves the audition marker along while the note panel's run plays.
    private readonly DispatcherTimer _noteRunTimer = PlayheadTimer.Build();

    // Watches the instrument sounding endlessly for dying out on its own.
    private readonly DispatcherTimer _endlessAuditionTimer = PlayheadTimer.Build();

    // Builds the window, wires every control to the song and shows the empty song a launch starts on, then applies the remembered settings.
    public MainWindow()
    {
        InitializeComponent();
        _heldShiftKey = new HeldShiftKey(this, ShowShiftHeld);
        SurroundField.FillDropDown(CellSurroundBox);

        _history = new EditHistory(_song);
        _sampleHistory = new SampleHistory(_song);
        ShowTitleBar();
        WireRoll();
        WireRuler();

        _rollView = new ScrollViewerView(RollScroll);
        _edgeScroll = new EdgeScroll(_rollView, () => new Size(RollMetrics.CellWidth, RollMetrics.CellHeight));
        WireEdgeScroll();
        _rollFollower = new PlayheadFollower(RollScroll);

        _laneRows = NewLaneRows();
        _lanes = _laneRows.Select(row => row.Lane).ToArray();
        _laneReorderDrag = new ListRowDrag((overLabels, _) => MoveDraggedLaneTo(overLabels.Y), static () => { });
        BuildLaneLabels();
        ReadLaneStackLimits();
        WireLaneDivider();
        HandSongToGrid();
        WireLanes();
        WireUnitLanes();
        WireInstrumentPanel();

        ShowStartingSong();
        WirePanels();
        ShowPanelLayout();
        WireTimersAndTransport();
        WireWindowKeys();
        HookFieldKeys();
        WireSongValueFields();
        WireTimeSignatureFields();
        WireCellEditorFields();
        HookScrollWheels();
        WireNotePanel();

        _placement = new WindowPlacement(this);
        ApplySettings();
        ReadAndShowInstall();

        Loaded += OnLoaded;
        Closed += OnWindowClosed;
    }

    // Hooks up everything the roll reports, the four channel lanes that draw a dragged note among them.
    private void WireRoll()
    {
        Roll.NoteAuditioned += OnNoteAuditioned;
        Roll.KeyAuditioned += OnKeyAuditioned;
        Roll.NoteReleased += ReleaseAudition;
        Keys.SemitonePressed += Roll.AuditionSemitone;
        Keys.SemitoneReleased += Roll.EndKeyAudition;
        Roll.SongEdited += () => MarkModified();
        Roll.SelectionChanged += OnSelectionChanged;
        Roll.SelectedNotesEdited += FollowSelectedNotes;
        Roll.ChannelPicked += SelectChannel;
        Roll.RollResized += RefreshRollExtent;
        Roll.NotesDragged += OnNotesDragged;

        foreach (var lane in new ChannelChangeLane[] { ChannelVolumes, Pans, Amplifies, ChannelEchoes })
        {
            lane.DraggedNotes = () => Roll.IsDraggingNotes ? Roll.Selection : [];
            Roll.NotesDragged += _ => lane.Refresh();
        }

        Roll.PlacementRefused += OnPlacementRefused;
        Roll.PanRequested += OnRollPan;
        Keys.PanRequested += OnRollPan;
        Roll.WheelDragRequested += Ruler.BeginWheelDrag;
    }

    // Hooks up the ruler's position, its two markers and its end toggle, and lays the markers over the ruler, the roll and the lanes.
    private void WireRuler()
    {
        Markers.ShowOver(RulerScroll, RollScroll, LaneScroll);
        Ruler.PositionRequested += SetPosition;
        Ruler.MarkerMoved += OnMarkerMoved;
        Ruler.MarkerDropped += () => MarkModified();
        Ruler.EndToggled += OnEndToggled;
    }

    // Scrolls the roll under a drag taken past an edge of the ruler, the roll or the keyboard.
    private void WireEdgeScroll()
    {
        Ruler.DragAt += x => _edgeScroll.FollowDrag(x, null, Ruler.FollowCursor);
        Ruler.DragEnded += _edgeScroll.Stop;
        Roll.DragAt += (x, y) => _edgeScroll.FollowDrag(x, y, Roll.FollowCursor);
        Roll.DragEnded += _edgeScroll.Stop;
        Keys.DragAt += y => _edgeScroll.FollowDrag(null, RollScroll.Offset.Y + y, Keys.FollowCursor);
        Keys.DragEnded += _edgeScroll.Stop;
    }

    // Hooks up every lane's edits, its drags past an edge, its pan and its selection.
    private void WireLanes()
    {
        foreach (var lane in _lanes)
        {
            lane.PlaceRequested += OnChangePlaced;
            lane.RemoveRequested += OnChangeRemoved;
            lane.MoveRequested += OnChangeMoved;
            lane.DragMoved += OnCellDragged;
            lane.RollResized += RefreshRollAndExtent;
            lane.DragAt += x => _edgeScroll.FollowDrag(x, null, lane.FollowCursor);
            lane.DragEnded += _edgeScroll.Stop;
            lane.PanRequested += OnLanePan;
            lane.WheelDragRequested += Ruler.BeginWheelDrag;
            lane.SelectionChanged += OnCellSelected;
        }
    }

    // Hooks up the lanes of units' edits, and makes a selection on any of them clear the roll's and every other one's.
    private void WireUnitLanes()
    {
        Staccatos.EditRequested += OnStaccatoEdited;
        ChannelEchoes.EditRequested += OnChannelEchoEdited;
        EchoUnits.EditRequested += OnEchoUnitEdited;

        foreach (var lane in UnitLanes)
        {
            lane.SelectionTaken += Roll.ClearSelection;
            foreach (var other in UnitLanes)
            {
                if (!ReferenceEquals(lane, other))
                {
                    lane.SelectionTaken += other.ClearUnitSelection;
                }
            }
        }
    }

    // Hooks up everything the instrument panel asks for.
    private void WireInstrumentPanel()
    {
        Instruments.InstrumentPicked += OnInstrumentPicked;
        Instruments.InstrumentEdited += OnInstrumentEdited;
        Instruments.RenameRequested += OnInstrumentRenameRequested;
        Instruments.DeleteRequested += OnDeleteInstrument;
        Instruments.CreateRequested += OnCreateInstrument;
        Instruments.ImportRequested += OnImportInstrument;
        Instruments.ExportRequested += OnExportInstrument;
        Instruments.ExportAllRequested += OnExportAllInstruments;
        Instruments.DuplicateRequested += OnDuplicateInstrument;
        Instruments.SelectNotesRequested += OnSelectInstrumentNotes;
        Instruments.AuditionRequested += OnInstrumentPlayPressed;
        Instruments.InstrumentRowAuditionRequested += AuditionInstrumentRow;
        Instruments.SampleRowAuditionRequested += AuditionInstrumentAtAuditionPitch;
        Instruments.InstrumentPickerRequested += ShowInstrumentPicker;
        Instruments.SampleEditRequested += OnInstrumentSampleEdit;
        Instruments.AuditionReleased += OnInstrumentPlayReleased;
    }

    // Shows the empty song a launch starts on in every control that shows the song.
    private void ShowStartingSong()
    {
        SetPosition(0);
        HideAllLanes();
        SnapBox.ItemsSource = SnapSize.Choices;
        ShowZoom();
        ShowSongState();
        ShowQuantization();
    }

    // Brings every control that shows the song's own state to the song as it now stands: the channel buttons, the instruments, the tempo, the snap,
    // the time signature, the song volume, the selected channel's lanes, the Song panel and the lanes.
    private void ShowSongState()
    {
        UpdateChannelButtons();
        RefreshInstruments();
        ShowSnap();
        ShowTimeSignature();
        ShowTempoField();
        ShowMasterVolumeField();
        ShowChannel();
        ShowSongPanelFields();
        RefreshLanes();
    }

    // Hooks up the playhead, the transport bar, the ARAM count, the note panel's audition and the instrument sounding endlessly, each of which runs on a timer or a layout pass.
    private void WireTimersAndTransport()
    {
        _playheadTimer.Tick += OnPlayheadTick;
        WireTransport();
        LayoutUpdated += (_, _) => AlignTransport();
        _aramCountTimer.Tick += (_, _) => CountAramUsage();
        _noteRunTimer.Tick += (_, _) => ShowNoteRunPosition();
        _endlessAuditionTimer.Tick += (_, _) => FollowEndlessAudition();
    }

    // Forgets the held keys when the window is left.
    private void WireWindowKeys()
    {
        Deactivated += (_, _) =>
        {
            _isChannelStepKeyDown = false;
            Roll.SetNodeInsertKey(false);
            ReleaseAuditionSpace();
        };
    }

    // Puts each value field's own key handler behind its arrow keys and its wheel.
    private void HookFieldKeys()
    {
        FieldSteps.OnKeys(VolumeBox, OnMasterVolumeKeyDown);
        FieldSteps.OnKeys(TempoBox, OnTempoKeyDown);
        FieldSteps.OnKeys(PositionBox, OnPositionKeyDown);
        FieldSteps.OnKeys(ToneBox, OnToneKeyDown);
        FieldSteps.OnKeys(LengthBox, OnLengthKeyDown);
        FieldSteps.OnKeys(NotePanelValues.GateRow.Box, OnGateKeyDown);
        FieldSteps.OnKeys(NotePanelValues.VelocityRow.Box, OnVelocityKeyDown);
        FieldSteps.OnKeys(NotePanelValues.DetuneRow.Box, OnFineTuneKeyDown);
        FieldSteps.OnKeys(NotePanelValues.VolumeRow.Box, OnNoteLeadKeyDown);
        FieldSteps.OnKeys(NotePanelValues.PanRow.Box, OnNotePanKeyDown);
        FieldSteps.OnKeys(NotePanelValues.AmplifyRow.Box, OnNoteAmplifyKeyDown);
    }

    // Hangs the roll's, the keys', the ruler's and the lanes' wheel handlers ahead of the controls under them.
    private void HookScrollWheels()
    {
        AddTunneledWheelHandler(RollScroll, OnRollScrollWheel);
        AddTunneledWheelHandler(Keys, OnKeysWheel);
        AddTunneledWheelHandler(RulerScroll, OnRulerScrollWheel);
        AddTunneledWheelHandler(LaneScroll, OnLaneScrollWheel);
    }

    // Hangs a wheel handler on a control ahead of the control's own.
    private static void AddTunneledWheelHandler(Control target, EventHandler<PointerWheelEventArgs> handler)
    {
        target.AddHandler(PointerWheelChangedEvent, handler, RoutingStrategies.Tunnel);
    }

    // Restores everything the settings carry, the window's own placement included, or maximizes the window where nothing is remembered.
    private void ApplySettings()
    {
        if (AppSettings.Load() is not { } settings)
        {
            WindowState = WindowState.Maximized;
            return;
        }

        _startupSongPath = settings.SongPath;
        _instrumentPlacement = settings.InstrumentPanel;
        _notePlacement = settings.NotePanel;
        _songPlacement = settings.SongPanel;
        _groupPlacement = settings.PanelGroup;
        _spcPlayerPanelPlacement = settings.SpcPlayerPanels;
        FollowPlaybackItem.IsChecked = settings.FollowPlayback;
        AuditionNotesItem.IsChecked = settings.AuditionNotes;
        Roll.DoesAuditionNotes = settings.AuditionNotes;
        ShowOtherChannelsItem.IsChecked = settings.ShowOtherChannels;
        Roll.IsShowingOtherChannels = settings.ShowOtherChannels;
        ShowAllTonesItem.IsChecked = settings.ShowAllTones;
        Keys.IsShowingEveryNoteName = settings.ShowAllTones;
        ShowRulerUnit(settings.IsRulerCountingTicks);
        EdgeScroll.PreferredStepColumns = Math.Clamp(settings.DragScrollColumns, EdgeScroll.MinStepColumns, EdgeScroll.MaxStepColumns);
        RollMetrics.ZoomStepPercent = settings.ZoomStepPercent;
        LoopOptions.IsShortestLoopPreferred = settings.IsShortestLoopPreferred;
        LoopOptions.DoesFindLoopFlattenVolume = settings.DoesFindLoopFlattenVolume;
        LoopOptions.DoesFindLoopSmoothCycles = settings.DoesFindLoopSmoothCycles;
        LoopOptions.SmoothCyclesEffectPercent = settings.SmoothCyclesEffectPercent;
        AppPaths.AmkDirectory = settings.AmkDirectory;
        AppPaths.RomPath = settings.RomPath;
        AppPaths.LastFolders = settings.LastFolders ?? new();
        _doesOpenMmlAfterExport = settings.OpenMmlAfterExport;
        _doesRomExtractionIncludeSmwOriginals = settings.DoesRomExtractionIncludeSmwOriginals;
        _doesCheckForUpdatesAtLaunch = settings.DoesCheckForUpdatesAtLaunch;
        _exportOptions = settings.MmlExport ?? MmlExportOptions.All;
        _placement.Apply(settings);
    }

    // The settings as they stand, to be saved as the window closes.
    private AppSettings CaptureSettings()
    {
        var settings = _placement.Capture();
        settings.InstrumentPanel = CapturePanelPlacement(_instrumentSlot);
        settings.NotePanel = CapturePanelPlacement(_noteSlot);
        settings.SongPanel = CapturePanelPlacement(_songSlot);
        settings.PanelGroup = _groupWindow?.CapturePlacement() ?? new PanelPlacement();
        settings.SpcPlayerPanels = _spcPlayer?.CaptureSidePanelPlacement() ?? _spcPlayerPanelPlacement;
        settings.FollowPlayback = FollowPlaybackItem.IsChecked;
        settings.AuditionNotes = AuditionNotesItem.IsChecked;
        settings.ShowOtherChannels = ShowOtherChannelsItem.IsChecked;
        settings.ShowAllTones = ShowAllTonesItem.IsChecked;
        settings.IsRulerCountingTicks = Ruler.IsCountingTicks;
        settings.DragScrollColumns = EdgeScroll.PreferredStepColumns;
        settings.ZoomStepPercent = RollMetrics.ZoomStepPercent;
        settings.IsShortestLoopPreferred = LoopOptions.IsShortestLoopPreferred;
        settings.DoesFindLoopFlattenVolume = LoopOptions.DoesFindLoopFlattenVolume;
        settings.DoesFindLoopSmoothCycles = LoopOptions.DoesFindLoopSmoothCycles;
        settings.SmoothCyclesEffectPercent = LoopOptions.SmoothCyclesEffectPercent;
        settings.AmkDirectory = AppPaths.AmkDirectory;
        settings.RomPath = AppPaths.RomPath;
        settings.LastFolders = AppPaths.LastFolders;
        settings.OpenMmlAfterExport = _doesOpenMmlAfterExport;
        settings.DoesRomExtractionIncludeSmwOriginals = _doesRomExtractionIncludeSmwOriginals;
        settings.DoesCheckForUpdatesAtLaunch = _doesCheckForUpdatesAtLaunch;
        settings.MmlExport = _exportOptions;
        settings.SongPath = _songPath;
        return settings;
    }

    // Puts the panels and the roll where they belong once the window is shown, warms up the pop-ups, starts the audio engine, asks for an AddmusicK
    // install where none the app can run is set, opens the song the last session had open and checks for a newer release.
    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(ScrollToReferenceRow, DispatcherPriority.Loaded);
        RestorePanels();
        Dispatcher.UIThread.Post(WarmUpPopups, DispatcherPriority.Background);

        if (!await TryStartAudio())
        {
            return;
        }

        ApplyPlayback();
        Dispatcher.UIThread.Post(WarmUpAudition, DispatcherPriority.Background);
        ShowAramTotals();
        await AskForAmkFolderWhereUnusable();

        if (_startupSongPath is { } path)
        {
            await OpenSongFileOrSayWhy(path);
        }

        await CheckForUpdatesAtLaunch();
    }

    // Opens the hidden drop-down and shuts it once it has been drawn, then does the same with each menu of the menu bar.
    private void WarmUpPopups()
    {
        WarmUpDropDown.ItemsSource = new[] { WarmUpDropDownItem };
        WarmUpDropDown.DropDownOpened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            WarmUpDropDown.IsDropDownOpen = false;
            WarmUpMenus(MainMenu.Items.OfType<MenuItem>().ToArray(), 0, FocusManager?.GetFocusedElement());
        }, DispatcherPriority.Background);
        WarmUpDropDown.IsDropDownOpen = true;
    }

    // Opens one menu of the menu bar too faint to see and shuts it once it has been drawn, then goes on to the next, and gives the focus back after the last.
    private void WarmUpMenus(MenuItem[] menus, int index, IInputElement? focusedBefore)
    {
        if (index >= menus.Length)
        {
            GiveFocusBackFromMenuBar(focusedBefore);
            return;
        }

        var menu = menus[index];

        // Shuts the menu once it has been drawn, puts it back to its own look and opens the next.
        void ShutOnceDrawn(object? sender, RoutedEventArgs e)
        {
            menu.SubmenuOpened -= ShutOnceDrawn;
            Dispatcher.UIThread.Post(() =>
            {
                MainMenu.Close();
                menu.Classes.Remove(ThemeClasses.WarmUp);
                WarmUpMenus(menus, index + 1, focusedBefore);
            }, DispatcherPriority.Background);
        }

        menu.Classes.Add(ThemeClasses.WarmUp);
        menu.SubmenuOpened += ShutOnceDrawn;
        menu.Open();
    }

    // Puts the focus back where it was before the warm-up opened the menus, where the menu bar still holds it.
    private void GiveFocusBackFromMenuBar(IInputElement? focusedBefore)
    {
        if (FocusManager?.GetFocusedElement() is not ILogical focused || !MainMenu.IsLogicalAncestorOf(focused))
        {
            return;
        }

        if (focusedBefore is null)
        {
            FocusManager.ClearFocus();
            return;
        }

        focusedBefore.Focus();
    }

    // Opens the audio device, saying so and answering false where it cannot be opened.
    private async Task<bool> TryStartAudio()
    {
        return await MessageDialog.TryOrSayFailure(this, () => _audio = new AudioEngine(new MiniAudioOutput()), WindowTitles.Failures.FailedToStart);
    }

    // Never closes on Escape.
    protected override bool IsClosedByEscape
    {
        get
        {
            return false;
        }
    }

    // Always asks before closing.
    protected override bool HasCloseQuestion
    {
        get
        {
            return true;
        }
    }

    // Saves the settings once the close is going ahead.
    protected override void OnCloseGoingAhead()
    {
        CaptureSettings().Save();
    }

    // Asks the song list's and the song's discard questions, answering whether the app may close.
    protected override async Task<bool> AskCloseQuestion()
    {
        return await ConfirmSongListAndSongBefore(QuestionsAndNotices.CloseAction);
    }

    // Asks the song list's and the song's discard questions before an action that closes the app, answering whether it may go on.
    private async Task<bool> ConfirmSongListAndSongBefore(string action)
    {
        if (_songList is not null && !await _songList.ConfirmClose())
        {
            return false;
        }

        return await ConfirmDiscard(action);
    }

    // Closes every other window the app has open and lets go of the audio device.
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        CloseSecondaryWindows();
        _audio?.Dispose();
    }

    // Opens the switches for the MML export's optimizations, counting the song data again as one moves.
    private async void OnMmlExportOptionsClick(object? sender, RoutedEventArgs e)
    {
        var box = new MmlExportOptionsWindow(_exportOptions);

        box.OptionsChanged += options =>
        {
            _exportOptions = options;
            ShowAramTotals();
        };

        await box.ShowDialog(this);
    }

    // Opens the app's own settings, applying each one as it is chosen.
    private async void OnPreferencesClick(object? sender, RoutedEventArgs e)
    {
        var preferences = new PreferencesWindow(EdgeScroll.PreferredStepColumns, RollMetrics.ZoomStepPercent, AppPaths.AmkDirectory, AppPaths.RomPath, _doesOpenMmlAfterExport,
            _doesRomExtractionIncludeSmwOriginals, _doesCheckForUpdatesAtLaunch);

        preferences.DragScrollChanged += columns => EdgeScroll.PreferredStepColumns = columns;
        preferences.ZoomSpeedChanged += percent => RollMetrics.ZoomStepPercent = percent;
        preferences.AmkDirectoryChanged += async directory => await AdoptAmkDirectory(directory, preferences);
        preferences.RomPathChanged += path => AppPaths.RomPath = path;
        preferences.OpenMmlAfterExportChanged += open => _doesOpenMmlAfterExport = open;
        preferences.SmwOriginalsInRomExtractionChanged += include => _doesRomExtractionIncludeSmwOriginals = include;
        preferences.CheckForUpdatesAtLaunchChanged += doesCheck => _doesCheckForUpdatesAtLaunch = doesCheck;
        await preferences.ShowDialog(this);
    }

    // Opens the About box.
    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        await new AboutWindow().ShowDialog(this);
    }

    // Asks for an AddmusicK install where the one set is missing or cannot be run, and points the app at the one chosen.
    private async Task AskForAmkFolderWhereUnusable()
    {
        if (AmkFolderCheck.RefusalOf(AppPaths.AmkDirectory) is null)
        {
            return;
        }

        if (await AmkFolderPromptWindow.Show(this, AppPaths.AmkDirectory) is not { } directory)
        {
            return;
        }

        await AdoptAmkDirectory(directory, this);
    }

    // Points the app at another AddmusicK install, building it first where it has never been built, and reads its budget and sample groups.
    private async Task AdoptAmkDirectory(string? directory, Window owner)
    {
        AppPaths.AmkDirectory = directory;

        if (AramBudget.AvailableBytes(directory) is null)
        {
            await BuildAmkInstall(owner);
        }

        AdoptInstallGroups(owner);
    }

    // Hands the screen's pixel ratio to the lanes once the window is on a screen, and again whenever it moves to one of another scale.
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        PushRenderScaling();
        ScalingChanged += (_, _) => PushRenderScaling();
    }

    // Hands the window's pixel ratio to the lanes and lays the stack out again where it changed.
    private void PushRenderScaling()
    {
        if (RollMetrics.RenderScaling == RenderScaling)
        {
            return;
        }

        RollMetrics.RenderScaling = RenderScaling;
        LayOutAndRefreshLanes();
    }
}
