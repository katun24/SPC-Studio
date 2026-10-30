using SpcStudio.Model;
using System.Diagnostics;
using System.IO;
using SpcStudio.Import;

namespace SpcStudio.Export;

// Writes the song as an .spc by having the user's AddmusicK compile it without a ROM and moving the dump it writes to where the user asked.
public static class SpcExport
{
    public const string DialogFilter = "SPC dump (*.spc)|*.spc" + DialogFilters.Separator + DialogFilters.AllFiles;
    public const string DumpExtension = ".spc";
    internal const string NoRomFlag = "-norom";
    private const string FallbackWorkingName = "SpcStudio";
    private const int CancelPollMilliseconds = 100;

    // Compiles the MML through AddmusicK and puts its dump at the destination, the song's samples having to be in the install already.
    public static void Run(string? amkRoot, string mml, string destinationPath, CancellationToken cancel)
    {
        string exePath = AmkRun.LocateExe(amkRoot);
        string root = amkRoot!;
        string musicFolder = WorkingSongFolder(root);
        string dumpFolder = Path.Combine(root, AmkInstallFolders.Dumps);

        try
        {
            Directory.CreateDirectory(musicFolder);
            Directory.CreateDirectory(dumpFolder);
        }
        catch (Exception exception)
        {
            throw AmkRunException.AmkFolderUnusable(exception.Message);
        }

        string workingName = FreeWorkingName(musicFolder, dumpFolder, destinationPath);
        string songPath = Path.Combine(musicFolder, workingName + MmlImporter.Extension);
        string dumpPath = Path.Combine(dumpFolder, workingName + DumpExtension);

        try
        {
            CompileIntoDump(exePath, root, mml, songPath, dumpPath, cancel);
            MoveDump(dumpPath, destinationPath);
        }
        finally
        {
            AmkRun.DeleteWorkingFile(songPath);
            AmkRun.DeleteWorkingFile(dumpPath);
        }
    }

    // The folder the working song is written into, which is the one its samples are looked for beside.
    public static string WorkingSongFolder(string amkRoot)
    {
        return Path.Combine(amkRoot, AmkInstallFolders.Music);
    }

    // Writes the working song into the install, runs AddmusicK over it and waits, refusing where no dump came out.
    private static void CompileIntoDump(string exePath, string amkRoot, string mml, string songPath, string dumpPath, CancellationToken cancel)
    {
        try
        {
            File.WriteAllText(songPath, mml);
        }
        catch (Exception exception)
        {
            throw AmkRunException.SongNotWritten(Path.GetDirectoryName(songPath), exception.Message);
        }

        WaitForExit(AmkRun.Start(exePath, amkRoot, NoRomFlag, Path.GetFileName(songPath)), cancel);

        if (!File.Exists(dumpPath))
        {
            throw AmkRunException.NoSpcWritten();
        }
    }

    // Moves the dump AddmusicK wrote to where the user asked for it, over whatever is there.
    private static void MoveDump(string dumpPath, string destinationPath)
    {
        try
        {
            File.Move(dumpPath, destinationPath, overwrite: true);
        }
        catch (Exception exception)
        {
            throw AmkRunException.SpcNotWritten(destinationPath, exception.Message);
        }
    }

    // A name no song in the music folder and no dump in the dump folder is using, built from the file the user asked for.
    private static string FreeWorkingName(string musicFolder, string dumpFolder, string destinationPath)
    {
        string wantedName = Path.GetFileNameWithoutExtension(destinationPath).Replace(".", "");
        if (wantedName.Length == 0)
        {
            wantedName = FallbackWorkingName;
        }

        for (int suffix = 0; ; suffix++)
        {
            string candidateName = suffix == 0 ? wantedName : $"{wantedName} {suffix}";

            if (!File.Exists(Path.Combine(musicFolder, candidateName + MmlImporter.Extension)) && !File.Exists(Path.Combine(dumpFolder, candidateName + DumpExtension)))
            {
                return candidateName;
            }
        }
    }

    // Waits for the run to end, and kills it where the wait is cancelled.
    private static void WaitForExit(Process process, CancellationToken cancel)
    {
        using (process)
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
}
