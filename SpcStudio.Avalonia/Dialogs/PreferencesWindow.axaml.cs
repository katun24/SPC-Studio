using Avalonia.Controls;
using Avalonia.Threading;
using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The app's own settings, each applied as it is changed and reported by event rather than written anywhere by the box itself.
public partial class PreferencesWindow : DialogWindow
{
    private const string ColumnNoun = "column";

    private bool _isShowingOutputDevices;

    // Raised with the drag scroll speed in columns a step, as its slider moves.
    public event Action<int>? DragScrollChanged;

    // Raised with the zoom speed in percent a notch, as its slider moves.
    public event Action<int>? ZoomSpeedChanged;

    // Raised with the AddmusicK install picked or typed, once it has passed, or with null where its field was emptied.
    public event Action<string?>? AmkDirectoryChanged;

    // Raised with the ROM picked or typed, once it has passed, or with null where its field was emptied.
    public event Action<string?>? RomPathChanged;

    // Raised with the name of the output device picked.
    public event Action<string>? OutputDeviceChanged;

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
        PathPicks.Wire(this, AmkFolderRow, PathPicks.AmkFolder, directory => AmkDirectoryChanged?.Invoke(directory));
        PathPicks.Wire(this, RomPathRow, PathPicks.Rom, path => RomPathChanged?.Invoke(path));
        ShowOutputDevices();
        OutputDeviceBox.SelectionChanged += OnOutputDeviceChanged;
        OutputDevices.ChoiceForgotten += OnOutputDeviceChoiceForgotten;
        Closed += (_, _) => OutputDevices.ChoiceForgotten -= OnOutputDeviceChoiceForgotten;
    }

    // Lists the playback devices the system offers, the one the app plays through picked.
    private void ShowOutputDevices()
    {
        var devices = OutputDevices.List();
        FlagGuard.RunWithFlagSet(ref _isShowingOutputDevices, () =>
        {
            OutputDeviceBox.ItemsSource = devices.Select(device => device.Name).ToList();
            OutputDeviceBox.SelectedItem = OutputDevices.InUseName(devices);
        });
    }

    // Reports the device picked, where the pick is the user's rather than the list being shown.
    private void OnOutputDeviceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isShowingOutputDevices || OutputDeviceBox.SelectedItem is not string deviceName)
        {
            return;
        }

        OutputDeviceChanged?.Invoke(deviceName);
    }

    // Lists the devices again on the window's own thread once a chosen device has given way to the system's default.
    private void OnOutputDeviceChoiceForgotten()
    {
        Dispatcher.UIThread.Post(ShowOutputDevices);
    }
}
