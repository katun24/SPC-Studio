using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpcStudio.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio;

// The Instrument Picker: every instrument file under the app's instruments folder as a tree of folders, followed live while the window is open, each
// instrument read off the window's thread once its row can be seen. Each instrument and each folder is a row that can be selected and dragged into
// another folder; an instrument's buttons add it to the song, duplicate it, delete its file, rename it and sound it, and a folder's add every
// instrument in it, duplicate it, delete it and rename it, its arrow or a double-click on it folding it.
public partial class InstrumentPickerWindow : StandaloneWindow
{
    private const double IndentPerTreeLevel = AppIcons.Size + Space.Px6;
    private const char FolderKeySeparator = '/';
    private const int RebuildDelayAfterDiskChangeMilliseconds = 250;

    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _rebuildTimer;
    private readonly Dictionary<Border, InstrumentLibraryEntry> _entriesByRow = new();
    private bool _isClosed;
    private bool _isLoadingShownInstruments;
    private bool _isPlayButtonHeld;
    private bool _isRebuildWaiting;
    private string? _selectedPath;
    private Border? _selectedRow;

    // Every instrument's row, in the tree's order.
    private readonly List<InstrumentRow> _instrumentRows = [];

    // Each folder's rows and chevron, by the folder's path under the instruments folder.
    private readonly Dictionary<string, FolderRows> _foldersByKey = new();

    // Which folders stand open: the song's own set, written as they are folded.
    private HashSet<string> _openFolderKeys = [];

    // The undo and redo stacks of the changes made here, kept by the main window for as long as the app runs.
    private InstrumentLibraryHistory _history = new();

    // Every instrument file read while the app runs, kept by the main window for as long as it runs.
    private InstrumentFileStore _instrumentFiles = new();

    // Builds the window and starts following the instruments folder for changes.
    public InstrumentPickerWindow()
    {
        InitializeComponent();
        _rowDrag = new ListRowDrag((overRows, overScrollViewer) => ShowDropFolder(DropFolderAt(overRows, overScrollViewer)), EndDrag);
        _rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RebuildDelayAfterDiskChangeMilliseconds) };
        _rebuildTimer.Tick += OnRebuildTimerTick;
        _watcher = WatchInstrumentsFolder();
    }

    // Builds the window over the history its changes are recorded in and the instrument files the app has read, and builds the tree from the
    // instruments folder.
    public InstrumentPickerWindow(InstrumentLibraryHistory history, InstrumentFileStore instrumentFiles) : this()
    {
        _history = history;
        _instrumentFiles = instrumentFiles;
        Rebuild();
    }

    // Raised by an instrument's '+' or a double-click on it, with the file to add to the song.
    public event Action<string>? AddRequested;

    // Raised by a folder's '+', with the folder and every instrument file under it to add to the song.
    public event Action<string, IReadOnlyList<string>>? FolderAddRequested;

    // Raised while a row's play button is held, with the instrument to sound.
    public event Action<InstrumentFile>? AuditionRequested;

    // Raised when that play button comes up.
    public event Action? AuditionReleased;

    // Whether a rebuild has to wait: a play button is held, a rename is open or a row is being dragged.
    private bool IsRebuildBlocked
    {
        get
        {
            return _isPlayButtonHeld || _renameBox is not null || _draggedRow is not null;
        }
    }

    // Takes the set of open folders to show and to write back to, folding every folder to match it and loading the instruments that come into sight.
    public void ShowFolders(HashSet<string> openFolderKeys)
    {
        _openFolderKeys = openFolderKeys;
        foreach (var (key, folder) in _foldersByKey)
        {
            ShowFold(folder, _openFolderKeys.Contains(key));
        }

        LoadShownInstruments();
    }

    // Closes an open rename without renaming anything when the window closes.
    protected override void OnCloseGoingAhead()
    {
        EndRename();
    }

    // Stops following the folder and loading instruments once the window has closed.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _isClosed = true;
        _rebuildTimer.Stop();
        _watcher?.Dispose();
    }

    // Steps the picker's own history on undo's key and redo's, and sounds the selected instrument for as long as the play key is held, as its play
    // button does, both ahead of whatever has the focus; the play key does nothing else here, a rename's field excepted, where it is a character.
    protected override void HandleWindowKeyDown(KeyEventArgs e)
    {
        var focused = FocusManager?.GetFocusedElement();
        if (WindowKeys.StepHistory(e, focused, Undo, Redo) || !WindowKeys.IsPlayKey(e, focused))
        {
            return;
        }

        e.Handled = true;
        if (_isPlayButtonHeld || SelectedInstrumentFile() is not { } file)
        {
            return;
        }

        OnPlayButtonPressed(file);
    }

    // Ends the selected instrument's sound when the play key comes up.
    protected override void HandleWindowKeyUp(KeyEventArgs e)
    {
        if (!Shortcuts.IsKeyOf(e, Shortcuts.Play))
        {
            return;
        }

        OnPlayButtonReleased();
    }

    // Starts a watcher over the instruments folder and everything under it, or answers none where the folder cannot be watched.
    private FileSystemWatcher? WatchInstrumentsFolder()
    {
        try
        {
            var watcher = new FileSystemWatcher(AppPaths.InstrumentsDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };

            watcher.Changed += (_, _) => PostDiskChange();
            watcher.Created += (_, _) => PostDiskChange();
            watcher.Deleted += (_, _) => PostDiskChange();
            watcher.Renamed += (_, _) => PostDiskChange();
            watcher.Error += (_, _) => PostDiskChange();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Hands a change on disk to the window's own thread.
    private void PostDiskChange()
    {
        Dispatcher.UIThread.Post(RestartRebuildTimer);
    }

    // Starts the wait a burst of changes on disk is gathered in, or starts it again.
    private void RestartRebuildTimer()
    {
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    // Builds the tree again once the changes on disk have settled.
    private void OnRebuildTimerTick(object? sender, EventArgs e)
    {
        _rebuildTimer.Stop();
        Rebuild();
    }

    // Builds the tree again from what is on disk, keeping the selected row selected and loading the instruments in sight, or marks it to be built
    // once nothing blocks it.
    private void Rebuild()
    {
        if (IsRebuildBlocked)
        {
            _isRebuildWaiting = true;
            return;
        }

        _isRebuildWaiting = false;
        _foldersByKey.Clear();
        _entriesByRow.Clear();
        _instrumentRows.Clear();
        TreeList.RowsPanel.Children.Clear();
        _selectedRow = null;

        var filesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddFolderContents(TreeList.RowsPanel, AppPaths.InstrumentsDirectory, 0, filesSeen);
        _instrumentFiles.KeepOnly(filesSeen);

        if (_selectedRow is null)
        {
            _selectedPath = null;
        }

        LoadShownInstruments();
    }

    // Loads the instruments whose rows can be seen, the top one first and one at a time off the window's thread, showing each as it arrives, until
    // none in sight is left or the window closes.
    private async void LoadShownInstruments()
    {
        if (_isLoadingShownInstruments)
        {
            return;
        }

        _isLoadingShownInstruments = true;
        while (!_isClosed && _instrumentRows.Find(row => row.Read is null && IsInSight(row.Row)) is { } nextRow)
        {
            string path = nextRow.Path;
            var read = await Task.Run(() => _instrumentFiles.Read(path));
            if (InstrumentRowAt(path) is { } loadedRow)
            {
                ShowRead(loadedRow, read);
            }
        }

        _isLoadingShownInstruments = false;
    }

    // Whether a row of the tree can be seen, which it can unless a folder above it is shut.
    private bool IsInSight(Control row)
    {
        for (var parent = row.GetVisualParent(); parent is not null && parent != TreeList.RowsPanel; parent = parent.GetVisualParent())
        {
            if (!parent.IsVisible)
            {
                return false;
            }
        }

        return true;
    }

    // The row of the instrument at a path, or null where the tree has none.
    private InstrumentRow? InstrumentRowAt(string path)
    {
        return _instrumentRows.Find(row => string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    // Shows what was read of an instrument on its row: its name in the text color and its play button enabled where the file could be read, both
    // disabled while it waits to be loaded or where it cannot be read.
    private static void ShowRead(InstrumentRow row, InstrumentFileRead? read)
    {
        row.Read = read;
        bool isReadable = read?.File is not null;
        row.PlayButton.IsEnabled = isReadable;
        if (isReadable)
        {
            row.Name.ClearValue(TextBlock.ForegroundProperty);
            return;
        }

        row.Name.Foreground = ThemeResource.TextDisabledBrush;
    }

    // Posts the rebuild that waited for whatever blocked it.
    private void PostWaitingRebuild()
    {
        if (_isRebuildWaiting)
        {
            Dispatcher.UIThread.Post(Rebuild);
        }
    }

    // Adds a folder's contents to a panel: its folders first, each with its own contents under it, and then its instruments, both in name order.
    private void AddFolderContents(Panel parentRows, string folderPath, int depth, HashSet<string> filesSeen)
    {
        string[] subfolderPaths;
        string[] instrumentPaths;

        try
        {
            (subfolderPaths, instrumentPaths) = InstrumentLibrary.FolderContents(folderPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (string subfolderPath in subfolderPaths)
        {
            var contentRows = new StackPanel();
            parentRows.Children.Add(BuildFolderRow(subfolderPath, depth, contentRows));
            parentRows.Children.Add(contentRows);
            AddFolderContents(contentRows, subfolderPath, depth + 1, filesSeen);
        }

        foreach (string path in instrumentPaths)
        {
            filesSeen.Add(path);
            parentRows.Children.Add(BuildInstrumentRow(path, depth));
        }
    }

    // A folder's row: its arrow, which folds the rows under it open or shut on a left press, its name and the '+', copy, bin and pencil buttons; a
    // double-click on it outside its buttons folds it as the arrow does.
    private Control BuildFolderRow(string folderPath, int depth, Panel contentRows)
    {
        string folderKey = FolderKeyOf(folderPath);
        var chevron = new IconShape
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = ThemeResource.TextSecondaryBrush,
        };

        var foldArea = new Border
        {
            Width = IndentPerTreeLevel,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = chevron,
        };

        var folder = new FolderRows(contentRows, chevron);
        _foldersByKey[folderKey] = folder;
        ShowFold(folder, _openFolderKeys.Contains(folderKey));
        foldArea.PointerPressed += (_, e) => OnFoldAreaPressed(foldArea, folderKey, folder, e);
        var entry = new InstrumentLibraryEntry(folderPath, IsFolder: true);
        var folderRow = BuildEntryRow(entry, NewRowName(Path.GetFileName(folderPath)), foldArea, depth, () => RequestAddAll(folderPath), playButton: null);
        ListRows.OnDoubleClickOutsideControls(folderRow, () => ToggleFold(folderKey, folder));
        return folderRow;
    }

    // Folds a folder open or shut on a left press on its arrow, and keeps the press from the row, which would select it.
    private void OnFoldAreaPressed(Border foldArea, string folderKey, FolderRows folder, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(foldArea).Properties.IsLeftButtonPressed)
        {
            return;
        }

        ToggleFold(folderKey, folder);
        e.Handled = true;
    }

    // Folds a folder open where it is shut and shut where it is open, writing the fold into the set of open folders and loading the instruments that
    // come into sight.
    private void ToggleFold(string folderKey, FolderRows folder)
    {
        bool isOpen = !_openFolderKeys.Contains(folderKey);
        if (isOpen)
        {
            _openFolderKeys.Add(folderKey);
        }
        else
        {
            _openFolderKeys.Remove(folderKey);
        }

        ShowFold(folder, isOpen);
        LoadShownInstruments();
    }

    // An instrument's row: its file's name and the '+', copy, bin, pencil and play buttons, its name and play button disabled until the file is loaded
    // and for good where it cannot be read; a left press on the row selects it and a single one starts a drag, and a double-click on it outside its
    // buttons adds it as the '+' does.
    private Control BuildInstrumentRow(string path, int depth)
    {
        var entry = new InstrumentLibraryEntry(path, IsFolder: false);
        var name = NewRowName(InstrumentFile.NameOf(path));
        var playButton = RowIconButton.BuildPlay(() => OnPlayButtonPressed(InstrumentRowAt(path)?.Read?.File), OnPlayButtonReleased, Tooltips.PlayInstrumentSpace);
        var row = BuildEntryRow(entry, name, null, depth + 1, () => RequestAdd(path), playButton);
        ListRows.OnDoubleClickOutsideControls(row, () => RequestAdd(path));
        var instrumentRow = new InstrumentRow(path, row, name, playButton);
        ShowRead(instrumentRow, _instrumentFiles.CurrentRead(path));
        _instrumentRows.Add(instrumentRow);
        return row;
    }

    // A row of the tree standing for a file or a folder: a list row stood in to its depth, holding its buttons at its right, a mark at its left where it has one and its name between them, which a
    // left press selects and a single one starts dragging, which is found again by what it stands for, and which shows itself selected where it was and opens its rename where one was awaited.
    private Border BuildEntryRow(InstrumentLibraryEntry entry, TextBlock name, Control? leftMark, int depth, Action add, Button? playButton)
    {
        var content = new DockPanel();
        var buttons = BuildRowButtons(entry, content, name, add, playButton);
        DockPanel.SetDock(buttons, Dock.Right);
        content.Children.Add(buttons);
        if (leftMark is not null)
        {
            DockPanel.SetDock(leftMark, Dock.Left);
            content.Children.Add(leftMark);
        }

        content.Children.Add(name);
        var row = ListRows.Build(content);
        row.Padding = IndentOf(depth);
        _entriesByRow[row] = entry;
        _rowDrag.Hook(row);
        row.PointerPressed += (_, e) => OnEntryRowPressed(row, entry, e);
        if (string.Equals(entry.Path, _selectedPath, StringComparison.OrdinalIgnoreCase))
        {
            Select(row, entry.Path);
        }

        StartRenameIfAwaited(entry, content, name);
        return row;
    }

    // A row's buttons: the '+' running the row's add, the copy duplicating what the row stands for, the bin deleting it and the pencil renaming it, then
    // the play button, or a gap as wide as one where the row has none, so every row's buttons stand in the same columns.
    private StackPanel BuildRowButtons(InstrumentLibraryEntry entry, DockPanel content, TextBlock name, Action add, Button? playButton)
    {
        var addButton = RowIconButton.BuildAdd(add, entry.IsFolder ? Tooltips.AddFolderInstrumentsToSong : Tooltips.AddInstrumentToSong);
        var duplicateButton = RowIconButton.Build(AppIcons.ContentCopy);
        duplicateButton.Click += (_, _) => Duplicate(entry);
        var deleteButton = RowIconButton.Build(AppIcons.Bin);
        deleteButton.Click += (_, _) => Delete(entry);
        var editButton = RowIconButton.Build(AppIcons.Pencil);
        editButton.Click += (_, _) => StartRename(entry, content, name);
        ToolTip.SetTip(duplicateButton, entry.IsFolder ? Tooltips.DuplicateFolder : Tooltips.DuplicateInstrument);
        ToolTip.SetTip(deleteButton, entry.IsFolder ? Tooltips.DeleteFolder : Tooltips.DeleteInstrument);
        ToolTip.SetTip(editButton, entry.IsFolder ? Tooltips.RenameFolder : Tooltips.RenameInstrument);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { addButton, duplicateButton, deleteButton, editButton } };
        if (playButton is null)
        {
            buttons.Margin = new Thickness(0, 0, ListRows.RowHeight, 0);
            return buttons;
        }

        buttons.Children.Add(playButton);
        return buttons;
    }

    // Asks for an instrument file to be added to the song.
    private void RequestAdd(string path)
    {
        AddRequested?.Invoke(path);
    }

    // Asks for every instrument file in a folder and its subfolders to be added to the song.
    private void RequestAddAll(string folderPath)
    {
        List<string> paths;

        try
        {
            paths = InstrumentLibrary.InstrumentPathsUnder(folderPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (paths.Count == 0)
        {
            return;
        }

        FolderAddRequested?.Invoke(folderPath, paths);
    }

    // Selects a row on a left press, and starts dragging it on a single one.
    private void OnEntryRowPressed(Border row, InstrumentLibraryEntry entry, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Select(row, entry.Path);
        StartDragOnSinglePress(row, entry, e);
    }

    // The selected instrument's file as it was loaded, or null where no instrument is selected, it is not loaded yet or the app cannot read it.
    private InstrumentFile? SelectedInstrumentFile()
    {
        if (_selectedRow is null)
        {
            return null;
        }

        return _instrumentRows.Find(row => row.Row == _selectedRow)?.Read?.File;
    }

    // Marks a row, a folder's or an instrument's, as the selected one, taking the mark off the row that had it.
    private void Select(Border row, string path)
    {
        if (_selectedRow is not null)
        {
            ListRows.ShowSelected(_selectedRow, false);
        }

        _selectedRow = row;
        _selectedPath = path;
        ListRows.ShowSelected(row, true);
    }

    // Sounds an instrument for as long as its play button is held, where it is loaded.
    private void OnPlayButtonPressed(InstrumentFile? file)
    {
        if (file is null)
        {
            return;
        }

        _isPlayButtonHeld = true;
        AuditionRequested?.Invoke(file);
    }

    // Ends the sound of a held play button coming up, and posts the rebuild that waited for it.
    private void OnPlayButtonReleased()
    {
        if (!_isPlayButtonHeld)
        {
            return;
        }

        _isPlayButtonHeld = false;
        AuditionReleased?.Invoke();
        PostWaitingRebuild();
    }

    // A folder's key in the set of open folders: its path under the instruments folder, its names parted by the key's own separator whatever the system's is.
    private static string FolderKeyOf(string folderPath)
    {
        string relativePath = FolderPaths.RelativePathUnder(AppPaths.InstrumentsDirectory, folderPath) ?? folderPath;
        return relativePath.Replace(Path.DirectorySeparatorChar, FolderKeySeparator);
    }

    // The folder a key in the set of open folders stands for.
    private static string FolderPathOf(string folderKey)
    {
        return Path.Combine(AppPaths.InstrumentsDirectory, folderKey.Replace(FolderKeySeparator, Path.DirectorySeparatorChar));
    }

    // Shows a folder's rows and points its chevron down where it is open, and hides them and points it right where it is shut.
    private static void ShowFold(FolderRows folder, bool isOpen)
    {
        folder.ContentRows.IsVisible = isOpen;
        folder.Chevron.Data = isOpen ? AppIcons.ChevronDown : AppIcons.ChevronRight;
    }

    // A row's name, centered in the row's height and cut short with an ellipsis where the row is too narrow for it.
    private static TextBlock NewRowName(string text)
    {
        return new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
    }

    // How far a row's contents stand in at a depth.
    private static Thickness IndentOf(int depth)
    {
        return new Thickness(depth * IndentPerTreeLevel, 0, 0, 0);
    }

    // A folder's rows under it and the chevron on its own row.
    private sealed record FolderRows(Panel ContentRows, IconShape Chevron);

    // An instrument's row with its name and play button, and what was read of its file, null until it is loaded.
    private sealed class InstrumentRow(string path, Border row, TextBlock name, Button playButton)
    {
        public string Path { get; } = path;
        public Border Row { get; } = row;
        public TextBlock Name { get; } = name;
        public Button PlayButton { get; } = playButton;
        public InstrumentFileRead? Read { get; set; }
    }
}
