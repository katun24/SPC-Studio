using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SpcStudio.Controls;
using SpcStudio.Export;
using SpcStudio.Import;
using SpcStudio.Theme;

namespace SpcStudio;

// AddmusicK's song list as two tables, the local songs and the global ones, edited a row at a time with an undo history of its own and saved back into the install, and the run of AddmusicK over it.
public partial class SongListWindow : StandaloneWindow
{
    public const double OpeningHeight = 640;
    public const double LeastHeight = 320;
    private const string NumberFormat = "X2";
    private const string RowColumns = "34,*,Auto,Auto,Auto,Auto";
    private const double BottomTolerance = 1;
    private const string AddSongButtonText = "Add Song";
    private const string AddingFromSmwcLine = "Adding Song From SMWCentral...";
    private const string SongsAddedStage = "songs added";

    private readonly Dictionary<int, SongListRow> _globalViews = [];
    private readonly Dictionary<int, SongListRow> _localViews = [];
    private readonly Button _globalAddButton = BuildAddButton();
    private readonly Button _localAddButton = BuildAddButton();
    private readonly SongListHistory _history = new(SongListState.Empty);
    private readonly string _amkRoot = "";
    private IReadOnlyDictionary<int, string> _savedGlobalSongs = new Dictionary<int, string>();

    // The .txt holding the song open in the app, exported first where it does not hold it as it is, its boxes standing over a given window, or null where
    // the export did not go.
    private readonly Func<Window, Task<string?>> _currentSongMmlPath = _ => Task.FromResult<string?>(null);

    // The .txt a picked .spc is transcribed and exported into under a progress host, its boxes standing over the host's window, or null where nothing was
    // written.
    private readonly Func<string, ProgressHost, Task<string?>> _spcSongMmlPath = (_, _) => Task.FromResult<string?>(null);

    // Runs AddmusicK over the install and has the app read it again, its boxes standing over a given window.
    private readonly Func<Window, Task> _rebuildInstall = _ => Task.CompletedTask;

    // Runs AddmusicK over the install and has the app read it again where its last build left out a file the ARAM readout is read out of, its boxes
    // standing over a given window, answering whether it did.
    private readonly Func<Window, Task<bool>> _rebuildInstallWhereUnbuilt = _ => Task.FromResult(false);

    // Has the app read the install again, its boxes standing over a given window.
    private readonly Action<Window> _readInstall = _ => { };

    // Add From SMWCentral's box.
    private static readonly TextPrompt SmwcSongPrompt = SmwcSong.Prompt(WindowTitles.AddFromSmwc, AddSongButtonText);

    // Builds the window with both tables empty.
    public SongListWindow()
    {
        InitializeComponent();
        _rowDrag = new ListRowDrag((overRows, _) => MoveDraggedSongTo(overRows.Y), EndDrag);

        _globalAddButton.Click += (_, _) => AddRow(GlobalList.Scroll, _history.Current.WithGlobalRowAdded());
        _localAddButton.Click += (_, _) => AddRow(LocalList.Scroll, _history.Current.WithLocalRowAdded());
        GlobalList.RowsPanel.Children.Add(_globalAddButton);
        LocalList.RowsPanel.Children.Add(_localAddButton);
        Tables.Add(SongListTab.Local, LocalList);
        Tables.Add(SongListTab.Global, GlobalList);
        Tables.Select(SongListTab.Local);
        Tables.TabSelected += ShowAddSongButtons;
        FitWidthTo(ButtonRow);
    }

    // Builds the window showing an install's list, with the ways to the .txt of the song open in the app and to the .txt of a transcribed .spc, and the ways
    // to build the install again, to build it again where its build files are missing and to read it again.
    public SongListWindow(string amkRoot, AmkSongList list, Func<Window, Task<string?>> currentSongMmlPath, Func<string, ProgressHost, Task<string?>> spcSongMmlPath,
        Func<Window, Task> rebuildInstall, Func<Window, Task<bool>> rebuildInstallWhereUnbuilt, Action<Window> readInstall) : this()
    {
        _amkRoot = amkRoot;
        _currentSongMmlPath = currentSongMmlPath;
        _spcSongMmlPath = spcSongMmlPath;
        _rebuildInstall = rebuildInstall;
        _rebuildInstallWhereUnbuilt = rebuildInstallWhereUnbuilt;
        _readInstall = readInstall;
        _savedGlobalSongs = list.GlobalSongs;
        _history.Reset(SongListState.FromList(list));
        ShowState();
    }

    // Raised by a row's load button with the full path of the song the row plays.
    public event Action<string>? LoadRequested;

    // Asks before closing only where there are changes not yet saved.
    protected override bool HasCloseQuestion
    {
        get
        {
            return _history.IsModified;
        }
    }

    // Whether the table showing is the global one.
    private bool IsGlobalShown
    {
        get
        {
            return Tables.SelectedTab == SongListTab.Global;
        }
    }

    // The table showing.
    private ListFrame ShownList
    {
        get
        {
            return IsGlobalShown ? GlobalList : LocalList;
        }
    }

    // Asks whether to save the changes not yet saved, answering whether the window may close.
    protected override Task<bool> AskCloseQuestion()
    {
        return MessageDialog.AskToSaveBefore(this, QuestionsAndNotices.SongListName, QuestionsAndNotices.CloseAction, Save);
    }

    // The window's own keys besides Escape: undo's and redo's keys step the window's own history, Save's and Add Song's keys do what their buttons do, and
    // Delete and Backspace take the song off the selected row of the table showing.
    protected override void HandleWindowKeyDown(KeyEventArgs e)
    {
        if (WindowKeys.StepHistory(e, FocusManager?.GetFocusedElement(), Undo, Redo) || Shortcuts.TryRun(e, AcceleratorsOf(e)) || !WindowKeys.IsDeleteKey(e.Key))
        {
            return;
        }

        e.Handled = true;
        ClearSelectedRow();
    }

    // Each Ctrl shortcut of this window's, with what it runs for a key press.
    private (string Shortcut, Action Run)[] AcceleratorsOf(KeyEventArgs e)
    {
        return
        [
            (Shortcuts.SaveSongList, () => OnSaveClick(this, e)),
            (Shortcuts.AddSong, () => AddPickedSongWherePressable(e)),
        ];
    }

    // Adds a row with a song picked in a file dialog as Add Song does, where Add Song can be pressed.
    private void AddPickedSongWherePressable(RoutedEventArgs e)
    {
        if (!AddSongButton.IsEnabled)
        {
            return;
        }

        OnAddSongClick(this, e);
    }

    // Takes the song off the selected row of the table showing, where a row is selected and no row is being dragged.
    private void ClearSelectedRow()
    {
        if (_rowDrag.DraggedRow is not null || ViewsOf(ShownList.RowsPanel).Values.FirstOrDefault(row => ListRows.IsShownSelected(row.View)) is not { } selected)
        {
            return;
        }

        ClearSong(selected.Number);
    }

    // Takes a row's song off it as one undo step, where clearing it can do anything.
    private void ClearSong(int number)
    {
        if (_history.Current.CanClear(number))
        {
            Apply(_history.Current.Cleared(number));
        }
    }

    // Puts back the tables as they were before the last edit.
    private void Undo()
    {
        if (!_history.CanUndo)
        {
            return;
        }

        _history.Undo();
        ShowState();
    }

    // Puts back the edit the last undo took away.
    private void Redo()
    {
        if (!_history.CanRedo)
        {
            return;
        }

        _history.Redo();
        ShowState();
    }

    // Takes the tables as an edit left them as an undo step and shows them.
    private void Apply(SongListState state)
    {
        if (_history.Record(state))
        {
            ShowState();
        }
    }

    // Takes a row added at a table's bottom as an undo step, keeping the table scrolled to the bottom where it was there already.
    private void AddRow(ScrollViewer scroll, SongListState state)
    {
        bool isScrolledToBottom = scroll.Offset.Y >= scroll.ScrollBarMaximum.Y - BottomTolerance;
        Apply(state);
        if (isScrolledToBottom)
        {
            scroll.UpdateLayout();
            scroll.ScrollToEnd();
        }
    }

    // Shows the history's current tables, each '+' in step with the numbers left to add, and the title marked where they are not saved.
    private void ShowState()
    {
        var state = _history.Current;
        ShowTable(GlobalList.RowsPanel, state);
        ShowTable(LocalList.RowsPanel, state);
        _globalAddButton.IsEnabled = state.CanAddGlobalRow;
        _localAddButton.IsVisible = state.CanAddLocalRow;
        ShowAddSongButtons();
        Title = _history.IsModified ? WindowTitles.Modified(WindowTitles.SongList) : WindowTitles.SongList;
    }

    // Lets Add Song, Add Current Song, Add From SMWCentral and Add From SPC be pressed only where the table showing can take a row at its bottom.
    private void ShowAddSongButtons()
    {
        bool canAddRow = CanAddRowAtBottom(_history.Current);
        AddSongButton.IsEnabled = canAddRow;
        AddCurrentSongButton.IsEnabled = canAddRow;
        AddFromSmwcButton.IsEnabled = canAddRow;
        AddFromSpcButton.IsEnabled = canAddRow;
    }

    // Puts a table's rows on screen in number order above its '+', keeping the row of every number still in it and writing each one afresh.
    private void ShowTable(StackPanel rowsPanel, SongListState state)
    {
        var views = ViewsOf(rowsPanel);
        var entries = EntriesOf(rowsPanel, state);

        var tableEntryNumbers = entries.Select(entry => entry.Number).ToHashSet();
        foreach (int number in views.Keys.Where(number => !tableEntryNumbers.Contains(number)).ToList())
        {
            rowsPanel.Children.Remove(views[number].View);
            views.Remove(number);
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!views.TryGetValue(entry.Number, out var row))
            {
                row = BuildRow(entry.Number);
                views[entry.Number] = row;
                rowsPanel.Children.Insert(i, row.View);
            }

            ShowRow(row, entry.ListName, state.CanClear(entry.Number));
        }
    }

    // The rows standing on screen in a table, by number.
    private Dictionary<int, SongListRow> ViewsOf(StackPanel rowsPanel)
    {
        return rowsPanel == GlobalList.RowsPanel ? _globalViews : _localViews;
    }

    // The rows a state holds in a table.
    private IReadOnlyList<SongListEntry> EntriesOf(StackPanel rowsPanel, SongListState state)
    {
        return rowsPanel == GlobalList.RowsPanel ? state.GlobalRows : state.LocalRows;
    }

    // A row for a song number, its buttons - shown while the pointer is over it or it is selected - acting on that number's song, a press on it
    // selecting it and dragging its song, and a double-click on it loading the song as its load button does.
    private SongListRow BuildRow(int number)
    {
        var numberText = new TextBlock { Text = number.ToString(NumberFormat), VerticalAlignment = VerticalAlignment.Center };
        var songText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var pickButton = RowIconButton.Build(AppIcons.FolderOpen);
        var loadButton = RowIconButton.Build(AppIcons.TrayDown);
        var editButton = RowIconButton.Build(AppIcons.Pencil);
        var clearButton = RowIconButton.Build(AppIcons.Bin);
        ToolTip.SetTip(pickButton, Tooltips.InsertSongIntoSlot);
        ToolTip.SetTip(loadButton, Tooltips.LoadSongInSpcStudio);
        ToolTip.SetTip(editButton, Tooltips.EditMml);
        ToolTip.SetTip(clearButton, Tooltips.RemoveSongFromSlot);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(RowColumns) };
        Control[] cells = [numberText, songText, pickButton, loadButton, editButton, clearButton];
        for (int column = 0; column < cells.Length; column++)
        {
            Grid.SetColumn(cells[column], column);
            grid.Children.Add(cells[column]);
        }

        var view = ListRows.Build(grid);
        MakeDraggable(view, number);
        var row = new SongListRow(number, view, songText, clearButton, loadButton, editButton);
        pickButton.Click += async (_, _) => await PickSong(number);
        loadButton.Click += (_, _) => Load(row);
        ListRows.OnDoubleClickOutsideControls(view, () => Load(row));
        editButton.Click += async (_, _) => await OpenMml(row);
        clearButton.Click += (_, _) => ClearSong(number);
        return row;
    }

    // The full path of the song a row plays, or null where it plays none.
    private string? SongPathOf(SongListRow row)
    {
        return row.ListName is { } listName ? AmkSongList.SongPath(_amkRoot, listName) : null;
    }

    // Asks for a row's song to be loaded into the app, where it plays one.
    private void Load(SongListRow row)
    {
        if (SongPathOf(row) is { } path)
        {
            LoadRequested?.Invoke(path);
        }
    }

    // Opens a row's song's .txt in the system's program for one, where the row plays a song.
    private async Task OpenMml(SongListRow row)
    {
        if (SongPathOf(row) is { } path)
        {
            await SystemOpen.OpenOrSayFailure(this, path, WindowTitles.Failures.FailedToOpenMml);
        }
    }

    // Writes a row's song into it, its load and edit buttons pressable only where it plays one and its clear button where clearing can do anything.
    private static void ShowRow(SongListRow row, string? listName, bool canClear)
    {
        bool hasSong = listName is not null;
        row.ListName = listName;
        row.SongText.Text = listName ?? "";
        row.LoadButton.IsEnabled = hasSong;
        row.EditButton.IsEnabled = hasSong;
        row.ClearButton.IsEnabled = canClear;
    }

    // The '+' that stands under a table's last row.
    private static Button BuildAddButton()
    {
        var button = RowIconButton.Build(AppIcons.Plus);
        ToolTip.SetTip(button, Tooltips.AddSlot);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Margin = new Thickness(0, Space.Px4, 0, 0);
        return button;
    }

    // Gives a row the song picked in a file dialog.
    private async Task PickSong(int number)
    {
        if (await PickListName() is { } listName)
        {
            Apply(_history.Current.WithSong(number, listName));
        }
    }

    // Adds rows at the bottom of the table showing with the songs of the .txt files or zips picked in a file dialog, as one undo step, adding nothing where
    // none is picked.
    private async void OnAddSongClick(object? sender, RoutedEventArgs e)
    {
        await AddPickedSongs(await OpenSongPicker(FilePickers.OpenFiles), WindowTitles.AddingSongs, UnpackedMmlPath);
    }

    // Adds a row at the bottom of the table showing with a song downloaded from SMWCentral by its number or its page's address typed into a box, as one undo step,
    // adding nothing where the box is closed or the song cannot be added.
    private async void OnAddFromSmwcClick(object? sender, RoutedEventArgs e)
    {
        if (await SmwcSong.AskForNumber(this, SmwcSongPrompt) is not { } songNumber || await DownloadedMmlPath(songNumber) is not { } mmlPath || await ListNameOf(this, mmlPath) is not { } listName)
        {
            return;
        }

        AddSongAtBottom(listName);
    }

    // Adds a row at the bottom of the table showing with the song open in the app, exported first where its .txt does not hold it as it is, as one undo step,
    // adding nothing where the export did not go or the song cannot be added.
    private async void OnAddCurrentSongClick(object? sender, RoutedEventArgs e)
    {
        if (await _currentSongMmlPath(this) is not { } mmlPath || await ListNameOf(this, mmlPath) is not { } listName)
        {
            return;
        }

        AddSongAtBottom(listName);
    }

    // Adds rows at the bottom of the table showing with the .spc files picked in a file dialog, each transcribed and exported into the music folder, as one
    // undo step, adding nothing where none is picked.
    private async void OnAddFromSpcClick(object? sender, RoutedEventArgs e)
    {
        await AddPickedSongs(await SpcFileOpening.PickPaths(this, WindowTitles.AddFromSpcPicker), WindowTitles.AddingSongsFromSpc, _spcSongMmlPath);
    }

    // Adds rows at the bottom of the table showing with the songs of picked files in the alphabetical order of their names, each file's .txt found in a
    // given way, as one undo step: a lone file as a single add, and several behind one progress box under a title, which adds none of them where it is
    // cancelled and says so in a box where the song list ran out of free song slots.
    private async Task AddPickedSongs(IReadOnlyList<string> pickedPaths, string batchTitle, Func<string, ProgressHost, Task<string?>> mmlPathOf)
    {
        var orderedPaths = pickedPaths.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
        if (orderedPaths.Count == 0)
        {
            return;
        }

        if (orderedPaths.Count == 1)
        {
            await AddPickedSong(orderedPaths[0], mmlPathOf);
            return;
        }

        var run = await ProgressDialog.RunOnInterfaceThread(this, batchTitle, ProgressDialog.CountLine(SongsAddedStage, 0, orderedPaths.Count),
            (batchBox, progress, cancel) => SongBatchOf(orderedPaths, mmlPathOf, batchBox, progress, cancel));
        if (run.IsCancelled || run.Value is not { } batch)
        {
            return;
        }

        AddRowsAtBottom(batch.State);
        if (batch.LeftOutCount > 0)
        {
            await MessageDialog.Show(this, QuestionsAndNotices.SongSlotsRanOut(batch.LeftOutCount), WindowTitles.Notices.NoFreeSongSlots);
        }
    }

    // Adds a row at the bottom of the table showing with the song of a picked file, its .txt found in a given way behind boxes of its own, as one undo step.
    private async Task AddPickedSong(string pickedPath, Func<string, ProgressHost, Task<string?>> mmlPathOf)
    {
        if (await ListNameOfPicked(pickedPath, ProgressHost.OwnBox(this), mmlPathOf) is not { } listName)
        {
            return;
        }

        AddSongAtBottom(listName);
    }

    // The tables with the songs of picked files added at the bottom of the table showing one after another, each file's .txt found in a given way as the
    // item a batch's box is at, and how many files were left out where the song list ran out of free song slots; a file whose song cannot be added is skipped.
    private async Task<SongBatch> SongBatchOf(IReadOnlyList<string> orderedPaths, Func<string, ProgressHost, Task<string?>> mmlPathOf, Window batchBox, JobProgress progress,
        CancellationToken cancel)
    {
        var state = _history.Current;
        for (int fileIndex = 0; fileIndex < orderedPaths.Count; fileIndex++)
        {
            cancel.ThrowIfCancellationRequested();
            if (!CanAddRowAtBottom(state))
            {
                return new SongBatch(state, orderedPaths.Count - fileIndex);
            }

            int doneCount = fileIndex;
            var host = ProgressHost.BatchItem(batchBox, itemFraction => progress(SongsAddedStage, doneCount, orderedPaths.Count, itemFraction), cancel);
            if (await ListNameOfPicked(orderedPaths[fileIndex], host, mmlPathOf) is { } listName)
            {
                state = WithSongAtBottom(state, listName);
            }

            progress(SongsAddedStage, doneCount + 1, orderedPaths.Count);
        }

        return new SongBatch(state, 0);
    }

    // Adds a row with a song at the bottom of the table showing, as one undo step.
    private void AddSongAtBottom(string listName)
    {
        AddRowsAtBottom(WithSongAtBottom(_history.Current, listName));
    }

    // Takes the tables with rows added at the bottom of the table showing as one undo step.
    private void AddRowsAtBottom(SongListState state)
    {
        AddRow(ShownList.Scroll, state);
    }

    // Whether the table showing can take a row at its bottom in given tables.
    private bool CanAddRowAtBottom(SongListState state)
    {
        return IsGlobalShown ? state.CanAddGlobalRow : state.CanAddLocalRow;
    }

    // Given tables with a row holding a song added at the bottom of the table showing.
    private SongListState WithSongAtBottom(SongListState state, string listName)
    {
        if (IsGlobalShown)
        {
            return state.WithGlobalRowAdded().WithSong(state.NextGlobalNumber, listName);
        }

        return state.WithLocalRowAdded().WithSong(state.NextLocalNumber, listName);
    }

    // The list name of the .txt or zip picked in a file dialog, or null where none was picked or it cannot be added, saying in a box why not.
    private async Task<string?> PickListName()
    {
        if (await OpenSongPicker(FilePickers.OpenFile) is not { } pickedPath)
        {
            return null;
        }

        return await ListNameOfPicked(pickedPath, ProgressHost.OwnBox(this), UnpackedMmlPath);
    }

    // Opens the picker for a song's .txt or zip in a given way of picking.
    private Task<T> OpenSongPicker<T>(Func<Window, string, string, string, string?, Task<T>> open)
    {
        return open(this, WindowTitles.AddSongToSongListPicker, SongZip.MmlOrZipDialogFilter, PickerId.PickListSong, AppPaths.MusicFolder);
    }

    // The list name of a picked file, its .txt found in a given way under a progress host, or null where none was found or it cannot be added, saying in a
    // box over the host's window why not.
    private async Task<string?> ListNameOfPicked(string pickedPath, ProgressHost host, Func<string, ProgressHost, Task<string?>> mmlPathOf)
    {
        if (await mmlPathOf(pickedPath, host) is not { } mmlPath)
        {
            return null;
        }

        return await ListNameOf(host.BoxOwner, mmlPath);
    }

    // The .txt a picked file is, a zip being unpacked into the install under a progress host first, or null where the unpacking was stopped or refused,
    // saying in a box over the host's window why.
    private async Task<string?> UnpackedMmlPath(string pickedPath, ProgressHost host)
    {
        var run = await MmlOrZip.MmlPathBehindProgressForZip(host, WindowTitles.ExtractSong, pickedPath, _amkRoot);
        return await MmlPathOrSayFailure(host.BoxOwner, run, WindowTitles.Failures.FailedToExtractSong);
    }

    // The .txt a song downloaded from SMWCentral unpacks into the install behind a progress box, or null where it was stopped or failed, saying in a box why.
    private async Task<string?> DownloadedMmlPath(int songNumber)
    {
        var failureTitle = WindowTitles.Failures.FailedToAddFromSmwc;
        var run = await ProgressDialog.RunUnderOneLine(this, WindowTitles.AddSongFromSmwc, AddingFromSmwcLine,
            (reportDoneFraction, cancel) => SmwcSong.MmlPathOf(songNumber, _amkRoot, this, new EqualStepsProgress(SmwcSong.StageCount, 0, reportDoneFraction), title => failureTitle = title, cancel),
            ex => ex.Message);
        return await MmlPathOrSayFailure(this, run, failureTitle);
    }

    // The .txt a job behind a progress box found, or null where it was stopped or failed, saying in a box over a window under a title why it failed.
    private static async Task<string?> MmlPathOrSayFailure(Window boxOwner, ProgressDialog.JobRun<string> run, FailureTitle failureTitle)
    {
        if (run.FailureMessage is { } failure)
        {
            await MessageDialog.Show(boxOwner, failure, failureTitle);
            return null;
        }

        return run.Value;
    }

    // The list name of a picked song, copied into the install first where it is outside the music folder, asking in boxes over a window before overwriting
    // the song or its samples, or null where it cannot be added, saying in a box over that window why not, or where an overwrite was refused.
    private async Task<string?> ListNameOf(Window boxOwner, string path)
    {
        try
        {
            return await Task.Run(() => AmkSongPick.ListNameOf(_amkRoot, path, MmlOrZip.OverwriteQuestionsOver(boxOwner)));
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception) when (exception is AmkSongListException or MmlImportException or IOException or UnauthorizedAccessException)
        {
            await MessageDialog.Show(boxOwner, exception.Message, WindowTitles.Failures.FailedToPickSong);
            return null;
        }
    }

    // Writes the list and builds the install again where its global songs changed, answering whether the list was written.
    private async Task<bool> Save()
    {
        if (await WriteList() is not { } list)
        {
            return false;
        }

        if (TakeGlobalSongsChange(list))
        {
            await _rebuildInstall(this);
        }

        return true;
    }

    // Writes both tables into the install's list and shows the list as written, saying in a box why not where it cannot be, answering the list written or
    // null where it was not.
    private async Task<AmkSongList?> WriteList()
    {
        var list = _history.Current.ToList();
        try
        {
            list.Write(_amkRoot);
        }
        catch (AmkSongListException exception)
        {
            await MessageDialog.Show(this, exception.Message, WindowTitles.Failures.FailedToSaveSongList);
            return null;
        }

        _history.Record(SongListState.FromList(list));
        _history.MarkSaved();
        ShowState();
        return list;
    }

    // Whether a list just written holds other global songs than the one AddmusicK last built from, which they are part of the program of, taking its
    // global songs as the ones built from.
    private bool TakeGlobalSongsChange(AmkSongList writtenList)
    {
        if (writtenList.GlobalSongs.SequenceEqual(_savedGlobalSongs))
        {
            return false;
        }

        _savedGlobalSongs = writtenList.GlobalSongs;
        return true;
    }

    // Saves the list.
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        await Save();
    }

    // Writes the list and runs AddmusicK over the ROM chosen in Preferences behind a box that waits for it, which puts every song in the list into it and
    // builds the install with them, has the app read the install again where the global songs changed, then says so in a box where it ended without an
    // error and shows what AddmusicK printed where it did not.
    private async void OnRunClick(object? sender, RoutedEventArgs e)
    {
        if (await WriteList() is not { } list)
        {
            return;
        }

        string? romPath = AppPaths.RomPath;
        var insertion = new AmkRunResult("", HasSucceeded: false);
        try
        {
            await MessageDialog.ShowWhile(this, QuestionsAndNotices.WaitingForAmk, WindowTitles.Plain.InsertSongsIntoRom, () => insertion = RomInsert.Run(_amkRoot, romPath));
        }
        catch (AmkRunException exception)
        {
            await MessageDialog.Show(this, exception.Message, WindowTitles.Failures.FailedToRunAmk);
            return;
        }

        bool hasGlobalSongsChange = TakeGlobalSongsChange(list);
        if (!await _rebuildInstallWhereUnbuilt(this) && hasGlobalSongsChange)
        {
            _readInstall(this);
        }

        if (insertion.HasSucceeded)
        {
            await MessageDialog.Show(this, QuestionsAndNotices.SongsInsertedIntoRom(Path.GetFileName(romPath)!), WindowTitles.Successes.SongsInsertedIntoRom);
            return;
        }

        await AmkOutputWindow.Show(this, insertion.Printed);
    }

    // What a batch of picked songs came to: the tables with every song added that could be, and how many picked files were left out for want of a free song slot.
    private sealed record SongBatch(SongListState State, int LeftOutCount);
}
