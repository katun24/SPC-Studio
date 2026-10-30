using System.IO;
using SpcStudio.Export;

namespace SpcStudio;

// Where the app's file dialogs open, each answering no folder where it has none to name, and the folders the app keeps beside its exe.
public static class AppPaths
{
    private const string NoFolder = "";
    private const string ProjectsFolderName = "Projects";
    private const string InstrumentsFolderName = "Instruments";
    private const string SettingsFolderName = "Settings";

    // The user's AddmusicK install, or null if none has been chosen yet.
    public static string? AmkDirectory { get; set; }

    // The ROM Insert Song Into ROM patches, or null if none has been chosen yet.
    public static string? RomPath { get; set; }

    // The folder each file dialog was last used in, keyed by the dialog's picker id.
    public static Dictionary<string, string> LastFolders { get; set; } = new();

    // The folder the running app's exe is in, without a separator at its end.
    public static string AppFolder
    {
        get
        {
            return Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        }
    }

    // The folder song files live in by default, beside the exe.
    private static string ProjectsDirectory
    {
        get
        {
            return Path.Combine(AppFolder, ProjectsFolderName);
        }
    }

    // The folder instrument files live in by default, beside the exe.
    public static string InstrumentsDirectory
    {
        get
        {
            return Path.Combine(AppFolder, InstrumentsFolderName);
        }
    }

    // The folder the app's settings are saved in, beside the exe.
    public static string SettingsDirectory
    {
        get
        {
            return Path.Combine(AppFolder, SettingsFolderName);
        }
    }

    // Where Open and Save As start.
    public static string ProjectsFolder
    {
        get
        {
            return ExistingOrNone(ProjectsDirectory);
        }
    }

    // Where the instrument panel's Import, Export and export of every instrument start.
    public static string InstrumentsFolder
    {
        get
        {
            return ExistingOrNone(InstrumentsDirectory);
        }
    }

    // The AddmusicK folder itself, where a picker starts that has no nearer folder in the install.
    public static string AmkFolder
    {
        get
        {
            return AmkDirectory is { } root ? ExistingOrNone(root) : NoFolder;
        }
    }

    // Where Import MML and Export MML start: the music folder under the AddmusicK folder.
    public static string MusicFolder
    {
        get
        {
            return AmkSubfolderOrRoot(AmkInstallFolders.Music);
        }
    }

    // Where Import Sample and the sample editor's two exports start: the samples folder under the AddmusicK folder.
    public static string SamplesFolder
    {
        get
        {
            return AmkSubfolderOrRoot(AmkInstallFolders.Samples);
        }
    }

    // Where the ROM picker starts: beside the ROM already chosen, or the AddmusicK folder where there is none.
    public static string RomFolder
    {
        get
        {
            if (RomPath is { } rom && Path.GetDirectoryName(rom) is { } folder && Directory.Exists(folder))
            {
                return folder;
            }

            return AmkFolder;
        }
    }

    // The folder a picker was last used in, or null if it never has been.
    public static string? LastFolder(string pickerId)
    {
        return LastFolders.TryGetValue(pickerId, out string? folder) ? folder : null;
    }

    // Records where a picker was used, a null or empty folder moving nothing.
    public static void RememberFolder(string pickerId, string? folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        LastFolders[pickerId] = folder;
    }

    // Creates the projects and instruments folders where they are not there, letting a failure pass.
    public static void CreateMissingAppFolders()
    {
        try
        {
            Directory.CreateDirectory(ProjectsDirectory);
            Directory.CreateDirectory(InstrumentsDirectory);
        }
        catch (Exception)
        {
        }
    }

    // The named folder inside the AddmusicK install, the install itself where it has no such folder, or no folder where no install is chosen.
    private static string AmkSubfolderOrRoot(string name)
    {
        if (AmkDirectory is not { } root)
        {
            return NoFolder;
        }

        string folder = Path.Combine(root, name);
        return Directory.Exists(folder) ? folder : AmkFolder;
    }

    // The path where it is a folder that exists, or no folder.
    private static string ExistingOrNone(string path)
    {
        return Directory.Exists(path) ? path : NoFolder;
    }
}
