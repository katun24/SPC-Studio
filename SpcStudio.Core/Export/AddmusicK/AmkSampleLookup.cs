using System.IO;

namespace SpcStudio.Export;

// Where AddmusicK reads a sample a song names: beside the song where the file is there, and otherwise under the install's samples folder.
internal static class AmkSampleLookup
{
    // The file AddmusicK reads for a sample named by its path under the samples folder, whether or not that file exists.
    public static string PathOf(string amkRoot, string songFolder, string sampleName)
    {
        string besideSong = BesideSong(songFolder, sampleName);
        if (File.Exists(besideSong))
        {
            return besideSong;
        }

        return InSamplesFolder(amkRoot, sampleName);
    }

    // The place beside the song AddmusicK looks first.
    public static string BesideSong(string songFolder, string sampleName)
    {
        return Path.Combine(songFolder, sampleName);
    }

    // The place under the install's samples folder AddmusicK looks second.
    public static string InSamplesFolder(string amkRoot, string sampleName)
    {
        return Path.Combine(amkRoot, AmkInstallFolders.Samples, sampleName);
    }
}
