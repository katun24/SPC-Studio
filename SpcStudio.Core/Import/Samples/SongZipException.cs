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

    // A song whose .txt the music folder already holds.
    public static SongZipException SongAlreadyInMusicFolder(string fileName)
    {
        return new SongZipException($"{fileName} is already in the music folder.");
    }

    // A sample folder the samples folder already holds.
    public static SongZipException SampleFolderAlreadyPresent(string folderName)
    {
        return new SongZipException($"\"{folderName}\" is already in the samples folder.");
    }

    // A zip whose song is inside a folder rather than in its root.
    public static SongZipException MmlNotInRoot()
    {
        return new SongZipException("The song's .txt is not in the root of the zip.");
    }

    // A zip with no song at all.
    public static SongZipException NoMml()
    {
        return new SongZipException("The zip has no song .txt file.");
    }

    // A zip with more than one song in its root, with their names.
    public static SongZipException SeveralMmls(IEnumerable<string> names)
    {
        return new SongZipException("The zip root has more than one song .txt file: " + SentenceList.Of(names) + ".");
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

    // A zip with samples in its root rather than in a folder, with their names.
    public static SongZipException LooseSamples(IEnumerable<string> names)
    {
        return new SongZipException("The zip root has .brr files outside a folder: " + SentenceList.Of(names) + ".");
    }

    // A zip with no folder of samples.
    public static SongZipException NoSampleFolder()
    {
        return new SongZipException("The zip has no folder of .brr samples.");
    }

    // A sample folder holding a file that is not a sample.
    public static SongZipException StrayFileInSampleFolder(string folderName, string strayPath)
    {
        return new SongZipException($"Folder \"{folderName}\" holds a file that is not a .brr: {strayPath}.");
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
