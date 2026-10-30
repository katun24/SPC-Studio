using SpcStudio.Model;

namespace SpcStudio;

// The text of every question and notice the app's windows put to the user, one entry each, with a parameter for each name or number the text mentions.
public static class QuestionsAndNotices
{
    public const string NewSongAction = "starting a new song";
    public const string OpenSongAction = "opening another song";
    public const string ImportMmlAction = "importing MML";
    public const string ImportMidiAction = "importing MIDI";
    public const string CloseAction = "closing";
    public const string TranscribeFromPlayerAction = "transcribing this SPC";
    public const string TranscribeFromMenuAction = "transcribing an SPC";
    public const string SongListName = "the song list";
    public const string ChooseAmkFolder = "Choose an AddmusicK folder for SPC Studio to work with. Without AddmusicK, some important features of SPC Studio will be disabled.";
    public const string NoSamplePath = "This song has no sample path. Name the folder under AddmusicK's samples directory that its samples should be written to.";
    public const string UpdateAction = "updating";
    public const string LatestVersionInUse = "You are using the latest " + WindowTitles.App + " version.";
    public const string UpdateCheckFailed = "An error occurred when checking for updates.";
    public const string UpdateDownloadFailed = "An error occurred when downloading the update.";
    private const int ShownSampleClashLimit = 12;
    private const string UpdateNotInstalled = "The update could not be installed.";

    // That reading an SPC found neither a loop nor an end in the time it reads for.
    public static string NoLoopOrEndFound(int readMinutes)
    {
        return $"No loop or end of the song was found in the first {readMinutes} minutes.";
    }

    // Whether to save a document before an action that throws its changes away.
    public static string SaveChangesBefore(string documentName, string action)
    {
        return $"Save changes to {documentName} before {action}?";
    }

    // Whether to overwrite the samples of other bytes already in the sample folder, naming as many of them as the box has room for.
    public static string OverwriteSamples(string samplePath, IReadOnlyList<string> clashes)
    {
        string shownClashNames = string.Join("\n", clashes.Take(ShownSampleClashLimit));
        if (clashes.Count > ShownSampleClashLimit)
        {
            shownClashNames += $"\nand {clashes.Count - ShownSampleClashLimit} more";
        }

        bool isOne = clashes.Count == 1;
        string whatIsHeld = isOne ? "a different sample of this name" : "different samples of these names";
        return $"{samplePath} already holds {whatIsHeld}:" + MessageDialog.ParagraphBreak + shownClashNames + MessageDialog.ParagraphBreak + $"Overwrite {(isOne ? "it" : "them")}?";
    }

    // Whether to overwrite a file already in the folder an export writes into.
    public static string OverwriteFile(string fileName)
    {
        return $"{fileName} is already in that folder. Overwrite it?";
    }

    // Whether to overwrite the MML an export goes to.
    public static string OverwriteMml(string mmlName)
    {
        return $"Overwrite existing MML '{mmlName}'?";
    }

    // Whether to delete a sample: what goes with it, and the undo history it costs.
    public static string DeleteSample(string sampleName, int instrumentCount, int noteCount)
    {
        return $"Delete {sampleName}?{SampleDeleteCascade(instrumentCount, noteCount)} Deleting a sample cannot be undone.";
    }

    // Whether to delete an instrument and the notes it plays.
    public static string DeleteInstrument(string instrumentName, int noteCount)
    {
        return $"{instrumentName} is played by {CountedNoun.Of(noteCount, CountedNoun.NoteNoun)}. Delete it and those notes?";
    }

    // Whether to delete an instrument file.
    public static string DeleteInstrumentFile(string instrumentName)
    {
        return $"Delete {instrumentName}?";
    }

    // Whether to delete a folder of instruments and everything in it.
    public static string DeleteFolder(string folderName, int instrumentCount)
    {
        return $"Delete the folder {folderName} and the {CountedNoun.Of(instrumentCount, NameRefusals.InstrumentNoun)} in it?";
    }

    // Whether to delete a channel and the notes on it.
    public static string DeleteChannel(int channelNumber, int noteCount)
    {
        return $"Channel {channelNumber} holds {CountedNoun.Of(noteCount, CountedNoun.NoteNoun)}. Delete it and those notes?";
    }

    // That the song cannot go to AddmusicK while a channel past the driver's own holds anything.
    public static string ExtraChannelsNotExported(int driverChannelCount)
    {
        return $"A song with more than {driverChannelCount} channels cannot be exported.";
    }

    // That the song cannot go to AddmusicK while it uses more instruments than the driver can play.
    public static string TooManyInstruments(int instrumentCount, int maxInstrumentCount)
    {
        return $"The song uses {instrumentCount} instruments, and AddmusicK can play at most {maxInstrumentCount} in one song.";
    }

    // That the song cannot go to AddmusicK while it uses more samples than the driver can play, counting its sample group's where it has one.
    public static string TooManySamples(int sampleCount, int groupSampleCount, int maxSampleCount)
    {
        string groupClause = groupSampleCount > 0 ? $", counting the {groupSampleCount} of its sample group" : "";
        return $"The song uses {sampleCount} samples{groupClause}, and AddmusicK can play at most {maxSampleCount} in one song.";
    }

    // That an instrument or a sample taken from the SPC Player was added to the song under a name.
    public static string AddedToSong(string name)
    {
        return $"{name} was added to the song.";
    }

    // Whether to download and install a newer release of the app.
    public static string UpdateAvailable(string version)
    {
        return $"{AppAtVersion(version)} is available. Download and install now?";
    }

    // What the progress box says while a newer release downloads.
    public static string DownloadingUpdate(string version)
    {
        return $"Downloading {AppAtVersion(version)}...";
    }

    // That an update could not be installed into the app's folder, which cannot be written to.
    public static string CannotWriteTo(string folder)
    {
        return $"Cannot write to {folder}.";
    }

    // That an update could not be installed, with what the system said.
    public static string UpdateInstallFailed(string systemMessage)
    {
        return $"{UpdateNotInstalled} {systemMessage}";
    }

    // The app named at a version, as the update's texts name a release.
    private static string AppAtVersion(string version)
    {
        return $"{WindowTitles.App} version {version}";
    }

    // The sentence naming the instruments and notes a sample's delete takes with it, or nothing where no instrument is built on the sample.
    private static string SampleDeleteCascade(int instrumentCount, int noteCount)
    {
        if (instrumentCount == 0)
        {
            return "";
        }

        string playedBy = $" It is played by {CountedNoun.Of(instrumentCount, NameRefusals.InstrumentNoun)}";
        if (noteCount == 0)
        {
            return $"{playedBy}, which will be deleted too.";
        }

        return $"{playedBy} and {CountedNoun.Of(noteCount, CountedNoun.NoteNoun)}, which will be deleted too.";
    }
}
