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
    private const string NumberFormat = "X2";
    private const string RowColumns = "34,*,Auto,Auto,Auto,Auto";
    private const double BottomTolerance = 1;

    private readonly Dictionary<int, SongListRow> _globalViews = [];
    private readonly Dictionary<int, SongListRow> _localViews = [];
    private readonly Button _globalAddButton = BuildAddButton();
    private readonly Button _localAddButton = BuildAddButton();
    private readonly SongListHistory _history = new(SongListState.Empty);
    private readonly string _amkRoot = "";

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
        Tables.TabSelected += ShowAddSongButton;
    }

    // Builds the window showing an install's list.
    public SongListWindow(string amkRoot, AmkSongList list) : this()
    {
        _amkRoot = amkRoot;
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

    // The rows of the table showing.
    private StackPanel ShownRowsPanel
    {
        get
        {
            return Tables.SelectedTab == SongListTab.Global ? GlobalList.RowsPanel : LocalList.RowsPanel;
        }
    }

    // Asks whether to save the changes not yet saved, answering whether the window may close.
    protected override Task<bool> AskCloseQuestion()
    {
        return MessageDialog.AskToSaveBefore(this, QuestionsAndNotices.SongListName, QuestionsAndNotices.CloseAction, Save);
    }

    // The window's own keys besides Escape: undo's and redo's keys step the window's own history, and Delete and Backspace take the song off the selected row of the table showing.
    protected override void HandleWindowKeyDown(KeyEventArgs e)
    {
        if (WindowKeys.StepHistory(e, Undo, Redo) || !WindowKeys.IsDeleteKey(e.Key))
        {
            return;
        }

        e.Handled = true;
        ClearSelectedRow();
    }

    // Takes the song off the selected row of the table showing, where a row is selected and no row is being dragged.
    private void ClearSelectedRow()
    {
        if (_rowDrag.DraggedRow is not null || ViewsOf(ShownRowsPanel).Values.FirstOrDefault(row => ListRows.IsShownSelected(row.View)) is not { } selected)
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
        ShowAddSongButton();
        Title = _history.IsModified ? WindowTitles.Modified(WindowTitles.SongList) : WindowTitles.SongList;
    }

    // Lets Add Song be pressed only where the table showing can take a row at its bottom.
    private void ShowAddSongButton()
    {
        var state = _history.Current;
        AddSongButton.IsEnabled = Tables.SelectedTab == SongListTab.Global ? state.CanAddGlobalRow : state.CanAddLocalRow;
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

    // Adds a row at the bottom of the table showing with the song picked in a file dialog, as one undo step, adding nothing where none is picked.
    private async void OnAddSongClick(object? sender, RoutedEventArgs e)
    {
        if (await PickListName() is not { } listName)
        {
            return;
        }

        var state = _history.Current;
        if (Tables.SelectedTab == SongListTab.Global)
        {
            AddRow(GlobalList.Scroll, state.WithGlobalRowAdded().WithSong(state.NextGlobalNumber, listName));
            return;
        }

        AddRow(LocalList.Scroll, state.WithLocalRowAdded().WithSong(state.NextLocalNumber, listName));
    }

    // The list name of the .txt or zip picked in a file dialog, or null where none was picked or it cannot be added, saying in a box why not.
    private async Task<string?> PickListName()
    {
        string? path = await FilePickers.OpenFile(this, WindowTitles.AddSongToSongListPicker, SongZip.MmlOrZipDialogFilter, PickerId.PickListSong, AppPaths.MusicFolder);
        if (path is null || await UnpackedMmlPath(path) is not { } mmlPath)
        {
            return null;
        }

        return await ListNameOf(mmlPath);
    }

    // The .txt a picked file is, a zip being unpacked into the install behind a progress box first, or null where the unpacking was stopped or refused, saying in a box why.
    private async Task<string?> UnpackedMmlPath(string pickedPath)
    {
        var run = await MmlOrZip.MmlPathBehindProgressForZip(this, WindowTitles.ExtractSong, pickedPath, _amkRoot);
        if (run.FailureMessage is { } failure)
        {
            await MessageDialog.Show(this, failure, WindowTitles.Failures.FailedToExtractSong);
            return null;
        }

        return run.Value;
    }

    // The list name of a picked song, copied into the install first where it is outside the music folder, or null where it cannot be added, saying in a box why not.
    private async Task<string?> ListNameOf(string path)
    {
        try
        {
            return AmkSongPick.ListNameOf(_amkRoot, path);
        }
        catch (Exception exception) when (exception is AmkSongListException or MmlImportException or IOException or UnauthorizedAccessException)
        {
            await MessageDialog.Show(this, exception.Message, WindowTitles.Failures.FailedToPickSong);
            return null;
        }
    }

    // Writes both tables into the install's list and shows the list as written, saying in a box why not where it cannot be, answering whether it was written.
    private async Task<bool> Save()
    {
        var list = _history.Current.ToList();
        try
        {
            list.Write(_amkRoot);
        }
        catch (AmkSongListException exception)
        {
            await MessageDialog.Show(this, exception.Message, WindowTitles.Failures.FailedToSaveSongList);
            return false;
        }

        _history.Record(SongListState.FromList(list));
        _history.MarkSaved();
        ShowState();
        return true;
    }

    // Saves the list.
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        await Save();
    }

    // Saves the list and starts AddmusicK over the ROM chosen in Preferences, which puts every song in the list into it.
    private async void OnRunClick(object? sender, RoutedEventArgs e)
    {
        if (!await Save())
        {
            return;
        }

        try
        {
            RomInsert.Run(_amkRoot, AppPaths.RomPath);
        }
        catch (AmkRunException exception)
        {
            await MessageDialog.Show(this, exception.Message, WindowTitles.Failures.FailedToRunAmk);
        }
    }
}
