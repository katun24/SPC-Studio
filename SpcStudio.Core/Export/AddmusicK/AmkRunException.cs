namespace SpcStudio.Export;

// Why a run of AddmusicK failed - it never started, or it ran and left no SPC where one was asked for - each made with the message that says which.
public sealed class AmkRunException : Exception
{
    // A refusal with the message that says why, and whether AddmusicK did run.
    private AmkRunException(string reason, bool hasAmkRun = false) : base(reason)
    {
        HasAmkRun = hasAmkRun;
    }

    // Whether AddmusicK did run, its own console window having said what went wrong.
    public bool HasAmkRun { get; }

    // No AddmusicK folder chosen, or one that is no longer there.
    public static AmkRunException NoAmkFolder()
    {
        return new AmkRunException("No AddmusicK folder is set. Choose one in Options > Preferences.");
    }

    // An AddmusicK folder with no exe in it.
    public static AmkRunException ExeMissing(string amkRoot)
    {
        return new AmkRunException($"There is no {AmkRun.ExeName} in {amkRoot}.");
    }

    // An AddmusicK folder holding the options file that would take the command line's place.
    public static AmkRunException OptionsFilePresent(string amkRoot)
    {
        return new AmkRunException(
            $"{amkRoot} has an {AmkRun.OptionsFileName}. AddmusicK reads its options out of that file instead of the command line, so it would ignore what it "
            + "is being asked to do here. Remove or rename the file and try again.");
    }

    // An exe that did not start.
    public static AmkRunException ExeDidNotStart()
    {
        return new AmkRunException($"{AmkRun.ExeName} did not start.");
    }

    // An exe the system could not start, with what it said.
    public static AmkRunException ExeCouldNotStart(string systemMessage)
    {
        return new AmkRunException($"Failed to start {AmkRun.ExeName}: {systemMessage}");
    }

    // No ROM chosen, or one that is no longer there.
    public static AmkRunException NoRom()
    {
        return new AmkRunException("No ROM is set, or the one that is set is no longer there. Choose one in Options > Preferences.");
    }

    // An AddmusicK folder that could not be prepared for a run, with what the system said.
    public static AmkRunException AmkFolderUnusable(string systemMessage)
    {
        return new AmkRunException($"Failed to use the AddmusicK folder: {systemMessage}");
    }

    // A song that could not be written into the install, with what the system said.
    public static AmkRunException SongNotWritten(string? folder, string systemMessage)
    {
        return new AmkRunException($"Failed to write the song into {folder}: {systemMessage}");
    }

    // A run that wrote no SPC.
    public static AmkRunException NoSpcWritten()
    {
        return new AmkRunException("AddmusicK did not write an SPC. Its console window says why.", hasAmkRun: true);
    }

    // An SPC that could not be copied to where it was asked for, with what the system said.
    public static AmkRunException SpcNotWritten(string destinationPath, string systemMessage)
    {
        return new AmkRunException($"Failed to write {destinationPath}: {systemMessage}", hasAmkRun: true);
    }
}
