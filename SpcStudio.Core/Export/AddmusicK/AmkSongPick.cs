using System.IO;
using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio.Export;

// A song picked for AddmusicK's list: the name the list gives it, the song and its samples copied into the install first where it is outside the music folder.
public static class AmkSongPick
{
    private const char ListNameSeparator = '/';

    // The name the list gives a picked song, refusing a song with a sample AddmusicK cannot find, and copying it and its samples into the install where
    // it is outside the music folder, asking before the song or the samples already there with other bytes are overwritten.
    public static string ListNameOf(string amkRoot, string pickedPath, InstallOverwriteConsent overwriteConsent)
    {
        string songPath = Path.GetFullPath(pickedPath);
        var samples = LocateSamples(amkRoot, songPath);
        string musicFolder = Path.Combine(amkRoot, AmkInstallFolders.Music);
        if (FolderPaths.RelativePathUnder(musicFolder, songPath) is { } relativePath)
        {
            return WithListSeparators(relativePath);
        }

        var sampleCopiesByDestination = new Dictionary<string, FileCopy>(StringComparer.OrdinalIgnoreCase);
        string songFolder = Path.GetDirectoryName(songPath)!;
        foreach (var (name, path) in samples.Where(sample => sample.Path == AmkSampleLookup.BesideSong(songFolder, sample.Name)))
        {
            if (CopyUnlessAlreadyThere(path, AmkSampleLookup.InSamplesFolder(amkRoot, name), WithListSeparators(name)) is { } sampleCopy)
            {
                sampleCopiesByDestination[sampleCopy.Destination] = sampleCopy;
            }
        }

        string songName = Path.GetFileName(songPath);
        var songCopy = CopyUnlessAlreadyThere(songPath, Path.Combine(musicFolder, songName), songName);
        var sampleCopies = sampleCopiesByDestination.Values.ToList();
        overwriteConsent.AskOrCancel(songCopy is { IsOverwriting: true } ? songName : null, sampleCopies.Where(copy => copy.IsOverwriting).Select(copy => copy.Name).ToList());
        CopyAll(songCopy is { } copy ? [.. sampleCopies, copy] : sampleCopies);
        return songName;
    }

    // A path with a slash between its folders, as AddmusicK's own list writes them.
    private static string WithListSeparators(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, ListNameSeparator);
    }

    // Each of the song's own samples by its name under the samples folder, with the file AddmusicK reads for it, refusing one it cannot find.
    private static List<(string Name, string Path)> LocateSamples(string amkRoot, string songPath)
    {
        var sampleList = SampleListOf(File.ReadAllText(songPath));
        string songFolder = Path.GetDirectoryName(songPath)!;
        var samples = new List<(string Name, string Path)>();
        foreach (string sampleFile in MmlSampleEntry.FileNamesIn(sampleList.Samples))
        {
            string name = Path.Combine(sampleList.SamplePath, sampleFile);
            string path = AmkSampleLookup.PathOf(amkRoot, songFolder, name);
            if (!File.Exists(path))
            {
                throw AmkSongListException.SampleMissing(name, AmkSampleLookup.BesideSong(songFolder, name), path);
            }

            samples.Add((name, path));
        }

        return samples;
    }

    // The song's sample list, read through the import's whole reading, refusing a song it refuses.
    private static MmlSampleList SampleListOf(string text)
    {
        var file = MmlParser.Parse(text);
        return new MmlSampleList(file.SamplePath, file.Samples);
    }

    // A file's copy to its place under the name the overwrite question shows it by, null where the place already holds the same bytes, and marked as
    // overwriting where it holds others.
    private static FileCopy? CopyUnlessAlreadyThere(string source, string destination, string name)
    {
        return InstallCopyPlan.UnlessAlreadyThere(destination, File.ReadAllBytes(source), isOverwriting => new FileCopy(source, destination, name, isOverwriting));
    }

    // Makes every copy, taking away every file and folder it made and putting back every file it overwrote where one fails.
    private static void CopyAll(List<FileCopy> copies)
    {
        var rollback = new InstallCopyRollback();
        try
        {
            foreach (var copy in copies)
            {
                rollback.NoteBeforeWriting(copy.Destination);
                File.Copy(copy.Source, copy.Destination, copy.IsOverwriting);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            rollback.TakeBack();
            throw AmkSongListException.CopyFailed(exception.Message);
        }
    }

    // A file copied into the install, with the name the overwrite question shows it by and whether it writes over a file already there.
    private readonly record struct FileCopy(string Source, string Destination, string Name, bool IsOverwriting = false);
}
