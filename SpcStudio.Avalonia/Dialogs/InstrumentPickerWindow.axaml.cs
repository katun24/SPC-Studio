using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SpcStudio.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio;

// The Instrument Picker: every instrument file under the app's instruments folder as a tree of folders, followed live while the window is open. Each
// instrument and each folder is a row that can be selected and dragged into another folder; an instrument's buttons add it to the song, duplicate it,
// delete its file, rename it and sound it, and a folder's add every instrument in it, duplicate it, delete it and rename it, its arrow folding it.
public partial class InstrumentPickerWindow : StandaloneWindow
{
    private const double IndentPerTreeLevel = AppIcons.Size + Space.Px6;
    private const char FolderKeySeparator = '/';
    private const int RebuildDelayAfterDiskChangeMilliseconds = 250;

    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _rebuildTimer;
    private readonly Dictionary<Border, InstrumentLibraryEntry> _entriesByRow = new();
    private bool _isPlayButtonHeld;
    private bool _isRebuildWaiting;
    private string? _selectedPath;
    private Border? _selectedRow;

    // Every instrument file read so far with the write time it was read at, the file being null where the app cannot read it.
    private readonly Dictionary<string, (DateTime WriteTime, InstrumentFile? File)> _filesRead = new(StringComparer.OrdinalIgnoreCase);

    // Each folder's rows and chevron, by the folder's path under the instruments folder.
    private readonly Dictionary<string, FolderRows> _foldersByKey = new();

    // Which folders stand open: the song's own set, written as they are folded.
    private HashSet<string> _openFolderKeys = [];

    // The undo and redo stacks of the changes made here, kept by the main window for as long as the app runs.
    private InstrumentLibraryHistory _history = new();

    // Builds the tree from the instruments folder and starts following the folder for changes.
    public InstrumentPickerWindow()
    {
        InitializeComponent();
        _rowDrag = new ListRowDrag((overRows, overScrollViewer) => ShowDropFolder(DropFolderAt(overRows, overScrollViewer)), EndDrag);
        _rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RebuildDelayAfterDiskChangeMilliseconds) };
        _rebuildTimer.Tick += OnRebuildTimerTick;
        _watcher = WatchInstrumentsFolder();
        Rebuild();
    }

    // Builds the window over the history its changes are recorded in.
    public InstrumentPickerWindow(InstrumentLibraryHistory history) : this()
    {
        _history = history;
    }

    // Raised by a '+' or a double-click on an instrument, with the files to add to the song.
    public event Action<IReadOnlyList<string>>? AddRequested;

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

    // Takes the set of open folders to show and to write back to, folding every folder to match it.
    public void ShowFolders(HashSet<string> openFolderKeys)
    {
        _openFolderKeys = openFolderKeys;
        foreach (var (key, folder) in _foldersByKey)
        {
            ShowFold(folder, _openFolderKeys.Contains(key));
        }
    }

    // Closes an open rename without renaming anything when the window closes.
    protected override void OnCloseGoingAhead()
    {
        EndRename();
    }

    // Stops following the folder once the window has closed.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _rebuildTimer.Stop();
        _watcher?.Dispose();
    }

    // Sounds the selected instrument for as long as the space bar is held, as its play button does, ahead of whatever has the focus; the space bar does
    // nothing else here, a rename's field excepted, where it is a character.
    protected override void HandleWindowKeyDown(KeyEventArgs e)
    {
        if (!WindowKeys.IsPlayKey(e, FocusManager?.GetFocusedElement()))
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

    // Ends the selected instrument's sound when the space bar comes up.
    protected override void HandleWindowKeyUp(KeyEventArgs e)
    {
        if (e.Key != Key.Space)
        {
            return;
        }

        OnPlayButtonReleased();
    }

    // Steps the picker's own history on undo's key and redo's, which a rename's field takes first while one is open.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (WindowKeys.StepHistory(e, Undo, Redo))
        {
            return;
        }

        base.OnKeyDown(e);
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

    // Builds the tree again from what is on disk, keeping the selected row selected, or marks it to be built once nothing blocks it.
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
        TreeList.RowsPanel.Children.Clear();
        _selectedRow = null;

        var filesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddFolderContents(TreeList.RowsPanel, AppPaths.InstrumentsDirectory, 0, filesSeen);

        foreach (string goneFile in _filesRead.Keys.Where(path => !filesSeen.Contains(path)).ToList())
        {
            _filesRead.Remove(goneFile);
        }

        if (_selectedRow is null)
        {
            _selectedPath = null;
        }
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
            parentRows.Children.Add(BuildInstrumentRow(path, ReadInstrumentFile(path), depth));
        }
    }

    // One instrument file, read again where it has changed since it was last read and taken from what was read otherwise; null where the app cannot read it.
    private InstrumentFile? ReadInstrumentFile(string path)
    {
        DateTime writeTime;

        try
        {
            writeTime = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (_filesRead.TryGetValue(path, out var read) && read.WriteTime == writeTime)
        {
            return read.File;
        }

        InstrumentFile? file;

        try
        {
            file = InstrumentFile.Load(path);
        }
        catch (Exception)
        {
            file = null;
        }

        _filesRead[path] = (writeTime, file);
        return file;
    }

    // A folder's row: its arrow, which folds the rows under it open or shut on a left press, its name and the '+', copy, bin and pencil buttons.
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
        return BuildEntryRow(entry, NewRowName(Path.GetFileName(folderPath)), foldArea, depth, () => RequestAddAll(folderPath), canAdd: true, playButton: null);
    }

    // Folds a folder open or shut on a left press on its arrow, writing the fold into the set of open folders, and keeps the press from the row, which would select it.
    private void OnFoldAreaPressed(Border foldArea, string folderKey, FolderRows folder, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(foldArea).Properties.IsLeftButtonPressed)
        {
            return;
        }

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
        e.Handled = true;
    }

    // An instrument's row: its file's name and the '+', copy, bin, pencil and play buttons, the '+' and the play button disabled where the app cannot read
    // the file; a left press on the row selects it and a single one starts a drag, and a double-click on it outside its buttons adds it as the '+' does.
    private Control BuildInstrumentRow(string path, InstrumentFile? file, int depth)
    {
        var entry = new InstrumentLibraryEntry(path, IsFolder: false);
        var name = NewRowName(InstrumentFile.NameOf(path));
        if (file is null)
        {
            name.Foreground = ThemeResource.TextDisabledBrush;
        }

        var playButton = RowIconButton.Build(AppIcons.Play);
        ToolTip.SetTip(playButton, Tooltips.PlayInstrument);
        playButton.IsEnabled = file is not null;
        if (file is not null)
        {
            HeldPress.HookPressAndRelease(playButton, () => OnPlayButtonPressed(file), OnPlayButtonReleased);
        }

        var row = BuildEntryRow(entry, name, null, depth + 1, () => RequestAdd(file, path), canAdd: file is not null, playButton);
        ListRows.OnDoubleClickOutsideControls(row, () => RequestAdd(file, path));
        return row;
    }

    // A row of the tree standing for a file or a folder: a list row stood in to its depth, holding its buttons at its right, a mark at its left where it has one and its name between them, which a
    // left press selects and a single one starts dragging, which is found again by what it stands for, and which shows itself selected where it was and opens its rename where one was awaited.
    private Border BuildEntryRow(InstrumentLibraryEntry entry, TextBlock name, Control? leftMark, int depth, Action add, bool canAdd, Button? playButton)
    {
        var content = new DockPanel();
        var buttons = BuildRowButtons(entry, content, name, add, canAdd, playButton);
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
    private StackPanel BuildRowButtons(InstrumentLibraryEntry entry, DockPanel content, TextBlock name, Action add, bool canAdd, Button? playButton)
    {
        var addButton = RowIconButton.Build(AppIcons.Plus);
        addButton.IsEnabled = canAdd;
        addButton.Click += (_, _) => add();
        var duplicateButton = RowIconButton.Build(AppIcons.ContentCopy);
        duplicateButton.Click += (_, _) => Duplicate(entry);
        var deleteButton = RowIconButton.Build(AppIcons.Bin);
        deleteButton.Click += (_, _) => Delete(entry);
        var editButton = RowIconButton.Build(AppIcons.Pencil);
        editButton.Click += (_, _) => StartRename(entry, content, name);
        ToolTip.SetTip(duplicateButton, entry.IsFolder ? Tooltips.DuplicateFolder : Tooltips.DuplicateInstrument);
        ToolTip.SetTip(addButton, entry.IsFolder ? Tooltips.AddFolderInstrumentsToSong : Tooltips.AddInstrumentToSong);
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

    // Asks for an instrument file to be added to the song, where the app can read it.
    private void RequestAdd(InstrumentFile? file, string path)
    {
        if (file is null)
        {
            return;
        }

        AddRequested?.Invoke([path]);
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

        AddRequested?.Invoke(paths);
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

    // The selected instrument's file as it was read, or null where no instrument is selected or the app cannot read it.
    private InstrumentFile? SelectedInstrumentFile()
    {
        if (_selectedRow is null || !_entriesByRow.TryGetValue(_selectedRow, out var entry) || entry.IsFolder)
        {
            return null;
        }

        return _filesRead.TryGetValue(entry.Path, out var read) ? read.File : null;
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

    // Sounds an instrument for as long as its play button is held.
    private void OnPlayButtonPressed(InstrumentFile file)
    {
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
}
