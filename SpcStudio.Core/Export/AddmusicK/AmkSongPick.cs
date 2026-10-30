using System.IO;
using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio.Export;

// A song picked for AddmusicK's list: the name the list gives it, the song and its samples copied into the install first where it is outside the music folder.
public static class AmkSongPick
{
    private const char ListNameSeparator = '/';

    // The name the list gives a picked song, refusing a song with a sample AddmusicK cannot find, and copying it and its samples into the install where
    // it is outside the music folder, refusing the copy whole where a file of another content is in the way.
    public static string ListNameOf(string amkRoot, string pickedPath)
    {
        string songPath = Path.GetFullPath(pickedPath);
        var samples = LocateSamples(amkRoot, songPath);
        string musicFolder = Path.Combine(amkRoot, AmkInstallFolders.Music);
        if (FolderPaths.RelativePathUnder(musicFolder, songPath) is { } relativePath)
        {
            return relativePath.Replace(Path.DirectorySeparatorChar, ListNameSeparator);
        }

        var copies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string songFolder = Path.GetDirectoryName(songPath)!;
        foreach (var (name, path) in samples.Where(sample => sample.Path == AmkSampleLookup.BesideSong(songFolder, sample.Name)))
        {
            AddCopy(copies, path, AmkSampleLookup.InSamplesFolder(amkRoot, name));
        }

        AddCopy(copies, songPath, Path.Combine(musicFolder, Path.GetFileName(songPath)));
        CopyAll(copies);
        return Path.GetFileName(songPath);
    }

    // Each of the song's own samples by its name under the samples folder, with the file AddmusicK reads for it, refusing one it cannot find.
    private static List<(string Name, string Path)> LocateSamples(string amkRoot, string songPath)
    {
        var file = MmlParser.Parse(File.ReadAllText(songPath));
        string songFolder = Path.GetDirectoryName(songPath)!;
        var samples = new List<(string Name, string Path)>();
        foreach (string sampleFile in file.SampleFiles)
        {
            string name = Path.Combine(file.SamplePath, sampleFile);
            string path = AmkSampleLookup.PathOf(amkRoot, songFolder, name);
            if (!File.Exists(path))
            {
                throw AmkSongListException.SampleMissing(name, AmkSampleLookup.BesideSong(songFolder, name), path);
            }

            samples.Add((name, path));
        }

        return samples;
    }

    // Adds a copy by its destination, leaving out one whose destination already holds the same bytes and refusing one holding others.
    private static void AddCopy(Dictionary<string, string> copies, string source, string destination)
    {
        if (!File.Exists(destination))
        {
            copies[destination] = source;
            return;
        }

        if (SampleExport.HoldsOtherBytes(destination, File.ReadAllBytes(source)))
        {
            throw AmkSongListException.FileInTheWay(destination);
        }
    }

    // Makes every copy, taking away every file and folder it made where one fails.
    private static void CopyAll(Dictionary<string, string> copies)
    {
        var createdFiles = new List<string>();
        var createdFolders = new List<string>();
        try
        {
            foreach (var (destination, source) in copies)
            {
                CreateFolders(Path.GetDirectoryName(destination)!, createdFolders);
                File.Copy(source, destination);
                createdFiles.Add(destination);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(createdFiles, createdFolders);
            throw AmkSongListException.CopyFailed(exception.Message);
        }
    }

    // Creates a folder and every missing one above it, noting each one made, the deepest last.
    private static void CreateFolders(string folder, List<string> createdFolders)
    {
        var missingFolders = new Stack<string>();
        for (string? current = folder; current is not null && !Directory.Exists(current); current = Path.GetDirectoryName(current))
        {
            missingFolders.Push(current);
        }

        foreach (string missingFolder in missingFolders)
        {
            Directory.CreateDirectory(missingFolder);
            createdFolders.Add(missingFolder);
        }
    }

    // Deletes the files a failed copy made and then its folders, the deepest first, going on past any that cannot be deleted.
    private static void DeleteQuietly(List<string> files, List<string> folders)
    {
        foreach (string file in files)
        {
            AmkRun.DeleteWorkingFile(file);
        }

        for (int i = folders.Count - 1; i >= 0; i--)
        {
            try
            {
                Directory.Delete(folders[i]);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
