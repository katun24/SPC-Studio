using SpcStudio.Model;
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

    // Compiles the MML through AddmusicK and puts its dump at the destination, the song's samples having to be in the install already.
    public static void Run(string? amkRoot, string mml, string destinationPath, CancellationToken cancel)
    {
        CompileWorkingSong(amkRoot, mml, Path.GetFileNameWithoutExtension(destinationPath), dumpPath => MoveDump(dumpPath, destinationPath), cancel);
    }

    // Compiles the MML through AddmusicK under a wanted name and throws its dump away, for a run wanted only for what it writes into the install.
    public static void CompileAndDiscard(string? amkRoot, string mml, string wantedName, CancellationToken cancel)
    {
        CompileWorkingSong(amkRoot, mml, wantedName, _ => { }, cancel);
    }

    // The folder the working song is written into, which is the one its samples are looked for beside.
    public static string WorkingSongFolder(string amkRoot)
    {
        return Path.Combine(amkRoot, AmkInstallFolders.Music);
    }

    // Writes the MML into the install under a free name built from a wanted one, has AddmusicK compile it and hands its dump to an action, deleting both
    // working files however it ends.
    private static void CompileWorkingSong(string? amkRoot, string mml, string wantedName, Action<string> takeDump, CancellationToken cancel)
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

        string workingName = FreeWorkingName(musicFolder, dumpFolder, wantedName);
        string songPath = Path.Combine(musicFolder, workingName + MmlImporter.Extension);
        string dumpPath = Path.Combine(dumpFolder, workingName + DumpExtension);

        try
        {
            CompileIntoDump(exePath, root, mml, songPath, dumpPath, cancel);
            takeDump(dumpPath);
        }
        finally
        {
            AmkRun.DeleteWorkingFile(songPath);
            AmkRun.DeleteWorkingFile(dumpPath);
        }
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

        string printed = AmkRun.Run(exePath, amkRoot, cancel, NoRomFlag, Path.GetFileName(songPath)).Printed;
        if (!File.Exists(dumpPath))
        {
            throw AmkRunException.NoSpcWritten(printed);
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

    // A name no song in the music folder and no dump in the dump folder is using, built from a wanted name.
    private static string FreeWorkingName(string musicFolder, string dumpFolder, string wantedName)
    {
        string baseName = wantedName.Replace(".", "");
        if (baseName.Length == 0)
        {
            baseName = FallbackWorkingName;
        }

        for (int suffix = 0; ; suffix++)
        {
            string candidateName = suffix == 0 ? baseName : $"{baseName} {suffix}";

            if (!File.Exists(Path.Combine(musicFolder, candidateName + MmlImporter.Extension)) && !File.Exists(Path.Combine(dumpFolder, candidateName + DumpExtension)))
            {
                return candidateName;
            }
        }
    }
}
