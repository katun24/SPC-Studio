using System.IO;
using System.Text.Json;
using SpcStudio.Controls;
using SpcStudio.Export;

namespace SpcStudio;

// What the app remembers between sessions in its own folder: the window's placement, the panels', the app's preferences and Loop Options, where each file
// dialog was last used and the song that was open.
public sealed class AppSettings
{
    private const string ProfileSettingsFolderName = "SpcStudio";
    private const string SettingsFileName = "settings.json";

    private static readonly string SettingsFilePath = Path.Combine(AppPaths.SettingsDirectory, SettingsFileName);
    private static readonly string ProfileSettingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProfileSettingsFolderName);
    private static readonly string ProfileSettingsFilePath = Path.Combine(ProfileSettingsFolder, SettingsFileName);
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
    public PanelPlacement InstrumentPanel { get; set; } = new();
    public PanelPlacement NotePanel { get; set; } = new();
    public PanelPlacement SongPanel { get; set; } = new();
    public PanelPlacement PanelGroup { get; set; } = new();
    public PanelPlacement SpcPlayerPanels { get; set; } = new();
    public bool FollowPlayback { get; set; } = true;
    public bool AuditionNotes { get; set; } = true;
    public bool ShowOtherChannels { get; set; } = true;
    public bool ShowAllTones { get; set; }
    public bool IsRulerCountingTicks { get; set; }
    public int DragScrollColumns { get; set; } = EdgeScroll.DefaultStepColumns;
    public int ZoomStepPercent { get; set; } = RollMetrics.DefaultZoomStepPercent;
    public bool IsShortestLoopPreferred { get; set; } = LoopOptions.DefaultIsShortestLoopPreferred;
    public bool DoesFindLoopFlattenVolume { get; set; } = LoopOptions.DefaultDoesFindLoopFlattenVolume;
    public bool DoesFindLoopSmoothCycles { get; set; } = LoopOptions.DefaultDoesFindLoopSmoothCycles;
    public int SmoothCyclesEffectPercent { get; set; } = LoopOptions.DefaultSmoothCyclesEffectPercent;
    public string? AmkDirectory { get; set; }
    public string? RomPath { get; set; }
    public bool OpenMmlAfterExport { get; set; } = true;
    public bool DoesRomExtractionIncludeSmwOriginals { get; set; }
    public bool DoesCheckForUpdatesAtLaunch { get; set; } = true;
    public MmlExportOptions MmlExport { get; set; } = MmlExportOptions.All;
    public string? SongPath { get; set; }

    // The folder each file dialog was last used in, keyed by the dialog's picker id.
    public Dictionary<string, string> LastFolders { get; set; } = new();

    // The saved settings, or null where there are none, they cannot be read or they would put the window at no size.
    public static AppSettings? Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFilePath));
            if (settings is null || settings.Width <= 0 || settings.Height <= 0)
            {
                return null;
            }

            return settings;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Moves the settings an older version kept in the user's profile into the app's folder, on the first launch after an update where the app's folder has none yet,
    // and deletes their folder in the profile where nothing else is in it, letting a failure pass.
    public static void MoveFromProfileAfterUpdate()
    {
        if (!UpdateInstall.HasJustUpdated || File.Exists(SettingsFilePath) || !File.Exists(ProfileSettingsFilePath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.SettingsDirectory);
            File.Move(ProfileSettingsFilePath, SettingsFilePath);
            if (!Directory.EnumerateFileSystemEntries(ProfileSettingsFolder).Any())
            {
                Directory.Delete(ProfileSettingsFolder);
            }
        }
        catch (Exception)
        {
        }
    }

    // Writes the settings to the app's folder, letting a failure pass.
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsDirectory);
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(this, IndentedJsonOptions));
        }
        catch (Exception)
        {
        }
    }
}
