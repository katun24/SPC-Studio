using System.IO;

namespace SpcStudio.Export;

// Runs AddmusicK over the install with -norom and one throwaway song, which writes the compiled driver the ARAM readout's budget is read out of.
public static class AmkBuild
{
    private const string WorkingDumpName = "SpcStudio Build.spc";

    private static readonly string SmallestSong = $"{MmlExporter.DialectLine}\n#0 r4\n";

    // Builds the install's driver, refusing with everything the SPC export refuses with.
    public static void BuildDriver(string? amkRoot)
    {
        string dumpPath = Path.Combine(Path.GetTempPath(), WorkingDumpName);

        try
        {
            SpcExport.Run(amkRoot, SmallestSong, dumpPath, CancellationToken.None);
        }
        finally
        {
            AmkRun.DeleteWorkingFile(dumpPath);
        }
    }
}
