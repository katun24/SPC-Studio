using System.IO;

namespace SpcStudio.Export;

// Whether a folder is an AddmusicK install the app can run, which choosing one and every run of AddmusicK both ask.
public static class AmkFolderCheck
{
    // Why the folder cannot be used as the AddmusicK install - no folder, no exe in it, or an options file that would take the command line's place - or null where it can.
    public static AmkRunException? RefusalOf(string? amkRoot)
    {
        if (string.IsNullOrEmpty(amkRoot) || !Directory.Exists(amkRoot))
        {
            return AmkRunException.NoAmkFolder();
        }

        if (!File.Exists(Path.Combine(amkRoot, AmkRun.ExeName)))
        {
            return AmkRunException.ExeMissing(amkRoot);
        }

        if (File.Exists(Path.Combine(amkRoot, AmkRun.OptionsFileName)))
        {
            return AmkRunException.OptionsFilePresent(amkRoot);
        }

        return null;
    }
}
