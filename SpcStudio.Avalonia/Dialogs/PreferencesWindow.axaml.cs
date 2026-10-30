using Avalonia.Interactivity;
using SpcStudio.Controls;
using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio;

// The app's own settings, each applied as it is changed and reported by event rather than written anywhere by the box itself.
public partial class PreferencesWindow : SettingsWidthDialogWindow
{
    private const string ColumnNoun = "column";

    // Raised with the drag scroll speed in columns a step, as its slider moves.
    public event Action<int>? DragScrollChanged;

    // Raised with the zoom speed in percent a notch, as its slider moves.
    public event Action<int>? ZoomSpeedChanged;

    // Raised with the AddmusicK install the folder picker returned.
    public event Action<string>? AmkDirectoryChanged;

    // Raised with the ROM the file picker returned.
    public event Action<string>? RomPathChanged;

    // Raised as the Open MML After Export box is ticked or cleared.
    public event Action<bool>? OpenMmlAfterExportChanged;

    // Raised as the Include SMW's Original Songs In ROM Extraction box is ticked or cleared.
    public event Action<bool>? SmwOriginalsInRomExtractionChanged;

    // Raised as the Check For Updates At Launch box is ticked or cleared.
    public event Action<bool>? CheckForUpdatesAtLaunchChanged;

    // Builds the box.
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    // Builds the box showing the settings given, each slider held to the range its setting takes.
    public PreferencesWindow(int dragScrollColumns, int zoomStepPercent, string? amkDirectory, string? romPath, bool openMmlAfterExport, bool doesRomExtractionIncludeSmwOriginals,
        bool doesCheckForUpdatesAtLaunch) : this()
    {
        DragScrollSetting.Show(new ValueRange(EdgeScroll.MinStepColumns, EdgeScroll.MaxStepColumns), dragScrollColumns, EdgeScroll.DefaultStepColumns,
            columns => CountedNoun.Of(columns, ColumnNoun), columns => DragScrollChanged?.Invoke(columns));
        ZoomSpeedSetting.Show(new ValueRange(RollMetrics.MinZoomStepPercent, RollMetrics.MaxZoomStepPercent), zoomStepPercent, RollMetrics.DefaultZoomStepPercent,
            percent => Percent.Text(percent), percent => ZoomSpeedChanged?.Invoke(percent));

        AmkFolderRow.ShownPath = amkDirectory;
        RomPathRow.ShownPath = romPath;
        ShowCheckBox(OpenMmlBox, openMmlAfterExport, open => OpenMmlAfterExportChanged?.Invoke(open));
        ShowCheckBox(SmwOriginalsBox, doesRomExtractionIncludeSmwOriginals, include => SmwOriginalsInRomExtractionChanged?.Invoke(include));
        ShowCheckBox(UpdateCheckBox, doesCheckForUpdatesAtLaunch, doesCheck => CheckForUpdatesAtLaunchChanged?.Invoke(doesCheck));
    }

    // Picks the AddmusicK install, starting at the one already chosen, and shows and reports it where it can be run.
    private async void OnAmkBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (await AmkFolderPick.PickUsableInto(this, AmkFolderRow) is not { } directory)
        {
            return;
        }

        AmkDirectoryChanged?.Invoke(directory);
    }

    // Picks the ROM, starting beside the one already chosen, and shows and reports it.
    private async void OnRomBrowseClick(object? sender, RoutedEventArgs e)
    {
        string? path = await FilePickers.OpenFile(this, WindowTitles.SelectRomPicker, RomInsert.DialogFilter, PickerId.RomFile, AppPaths.RomFolder);
        if (path is null)
        {
            return;
        }

        RomPathRow.ShownPath = path;
        RomPathChanged?.Invoke(path);
    }
}
