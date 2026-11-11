using System.Diagnostics;
using System.IO;

namespace SpcStudio.Export;

// Runs the user's AddmusicK in its install folder with no window and no keyboard, which the ROM insert and the SPC export both do, collecting what it prints.
internal static class AmkRun
{
    public const string OptionsFileName = "Addmusic_options.txt";
    private const string WindowsExeName = "AddmusicK.exe";
    private const string OtherSystemsExeName = "AddmusicK";
    private const string NoBlockFlag = "-noblock";
    private const string TerminalVariable = "TERM";
    private const string TextOnlyTerminal = "dumb";
    private const int CancelPollMilliseconds = 100;

    // The program's file name on the system the app runs on.
    public static string ExeName
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsExeName;
            }

            return OtherSystemsExeName;
        }
    }

    // The exe to run, refusing where there is no install, no exe in it, or an options file that would take the command line's place.
    public static string LocateExe(string? amkRoot)
    {
        if (AmkFolderCheck.RefusalOf(amkRoot) is { } refusal)
        {
            throw refusal;
        }

        return Path.Combine(amkRoot!, ExeName);
    }

    // Runs the exe in the install folder until it exits and answers everything it printed, refusing where it would not start and killing it where the
    // wait is cancelled.
    public static string Run(string exePath, string amkRoot, CancellationToken cancel, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = amkRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment[TerminalVariable] = TextOnlyTerminal;
        startInfo.ArgumentList.Add(NoBlockFlag);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Started(startInfo);
        process.StandardInput.Close();
        var printed = process.StandardOutput.ReadToEndAsync();
        var printedAsErrors = process.StandardError.ReadToEndAsync();
        WaitForExit(process, cancel);
        return (printed.Result + printedAsErrors.Result).Trim();
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

    // The process started, refusing where it would not start.
    private static Process Started(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw AmkRunException.ExeDidNotStart();
        }
        catch (Exception exception) when (exception is not AmkRunException)
        {
            throw AmkRunException.ExeCouldNotStart(exception.Message);
        }
    }

    // Waits for the run to end, and kills it where the wait is cancelled.
    private static void WaitForExit(Process process, CancellationToken cancel)
    {
        while (!process.WaitForExit(CancelPollMilliseconds))
        {
            if (!cancel.IsCancellationRequested)
            {
                continue;
            }

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new OperationCanceledException(cancel);
        }
    }
}
