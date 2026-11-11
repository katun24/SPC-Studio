using SpcStudio.Model;

namespace SpcStudio.Import;

// Why a zip was refused, each made with the message that says why as a whole sentence.
public sealed class SongZipException : Exception
{
    private SongZipException(string reason) : base(reason)
    {
    }

    // No AddmusicK folder chosen, or one that is no longer there.
    public static SongZipException NoAmkFolder()
    {
        return new SongZipException("No AddmusicK folder has been chosen in Preferences.");
    }

    // An install missing one of the folders a song is unpacked into.
    public static SongZipException InstallFolderMissing(string folderName, string folderPath)
    {
        return new SongZipException($"The AddmusicK folder has no {folderName} folder: {folderPath}.");
    }

    // A sample folder whose place in the samples folder a file already takes, by its name.
    public static SongZipException AlreadyInSamplesFolder(string nameUnderSamplesFolder)
    {
        return new SongZipException($"\"{nameUnderSamplesFolder}\" is already in the samples folder.");
    }

    // A zip with no song at all.
    public static SongZipException NoMml()
    {
        return new SongZipException("The zip contains no valid MML file.");
    }

    // A zip whose sample groups define one group twice with different samples.
    public static SongZipException GroupDefinedTwice(string groupName)
    {
        return new SongZipException($"The zip defines the sample group {SampleGroups.DirectiveOf(groupName)} twice, with different samples.");
    }

    // A zip's sample group whose name the install's groups file already gives a group with different samples.
    public static SongZipException GroupDiffersFromInstall(string groupName)
    {
        return new SongZipException(
            $"The zip's sample group {SampleGroups.DirectiveOf(groupName)} has different samples from the group of that name in AddmusicK's {SampleGroups.FileName}.");
    }

    // A zip's sample group naming a sample that is neither in the zip nor in the install's samples folder.
    public static SongZipException GroupSampleMissing(string groupName, string samplePath)
    {
        return new SongZipException($"The zip's sample group {SampleGroups.DirectiveOf(groupName)} names \"{samplePath}\", which is neither in the zip nor in the samples folder.");
    }

    // A zip that could not be unpacked, with what the system said.
    public static SongZipException ExtractFailed(string systemMessage)
    {
        return new SongZipException($"Failed to extract the zip: {systemMessage}");
    }

    // A file that is not a zip archive.
    public static SongZipException NotZip(string fileName)
    {
        return new SongZipException($"{fileName} is not a valid zip archive.");
    }
}
