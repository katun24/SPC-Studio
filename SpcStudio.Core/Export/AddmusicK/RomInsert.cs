using SpcStudio.Model;
using System.IO;

namespace SpcStudio.Export;

// Runs the user's AddmusicK on the user's ROM, which inserts every song in its list into it.
public static class RomInsert
{
    public const string DialogFilter = "SNES ROM (*.smc;*.sfc)|*.smc;*.sfc" + DialogFilters.Separator + DialogFilters.AllFiles;

    // Starts AddmusicK on the ROM and returns once it is running, refusing everything that stops the run happening at all.
    public static void Run(string? amkRoot, string? romPath)
    {
        string exePath = AmkRun.LocateExe(amkRoot);

        if (string.IsNullOrEmpty(romPath) || !File.Exists(romPath))
        {
            throw AmkRunException.NoRom();
        }

        AmkRun.Start(exePath, amkRoot!, romPath).Dispose();
    }
}
