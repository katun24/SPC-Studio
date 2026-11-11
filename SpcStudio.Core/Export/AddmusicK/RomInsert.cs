using System.IO;

namespace SpcStudio.Export;

// Runs the user's AddmusicK on the user's ROM, which inserts every song in its list into it.
public static class RomInsert
{
    // Runs AddmusicK on the ROM until it exits and answers what it printed, refusing everything that stops the run happening at all.
    public static string Run(string? amkRoot, string? romPath)
    {
        string exePath = AmkRun.LocateExe(amkRoot);

        if (string.IsNullOrEmpty(romPath) || !File.Exists(romPath))
        {
            throw AmkRunException.NoRom();
        }

        return AmkRun.Run(exePath, amkRoot!, CancellationToken.None, romPath);
    }
}
