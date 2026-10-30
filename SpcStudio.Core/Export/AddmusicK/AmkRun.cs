using System.Diagnostics;
using System.IO;

namespace SpcStudio.Export;

// Starts the user's AddmusicK in its install folder with a console window of its own, which the ROM insert and the SPC export both do.
internal static class AmkRun
{
    public const string ExeName = "AddmusicK.exe";
    public const string OptionsFileName = "Addmusic_options.txt";

    // The exe to run, refusing where there is no install, no exe in it, or an options file that would take the command line's place.
    public static string LocateExe(string? amkRoot)
    {
        if (AmkFolderCheck.RefusalOf(amkRoot) is { } refusal)
        {
            throw refusal;
        }

        return Path.Combine(amkRoot!, ExeName);
    }

    // Starts the exe in the install folder with its own console, refusing where the process would not start.
    public static Process Start(string exePath, string amkRoot, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = amkRoot,
            UseShellExecute = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(startInfo) ?? throw AmkRunException.ExeDidNotStart();
        }
        catch (Exception exception) when (exception is not AmkRunException)
        {
            throw AmkRunException.ExeCouldNotStart(exception.Message);
        }
    }

    // Deletes a working file a run was given or left behind, leaving it where it is when it cannot be deleted.
    public static void DeleteWorkingFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
