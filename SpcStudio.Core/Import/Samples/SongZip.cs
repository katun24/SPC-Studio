using System.IO;
using System.IO.Compression;
using SpcStudio.Audio;
using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

// Unpacks a shared song - one .txt, the folders of .brr samples it plays and any sample groups it brings - into an AddmusicK install, checking the
// whole archive before writing a byte and never overwriting anything.
public static class SongZip
{
    public const string Extension = ".zip";
    public const string MmlOrZipDialogFilter = "AddmusicK MML or zip (*.txt;*.zip)|*.txt;*.zip" + DialogFilters.Separator + DialogFilters.AllFiles;

    // Whether a picked file is a zip rather than a .txt.
    public static bool IsZip(string path)
    {
        return FileExtension.Matches(path, Extension);
    }

    // Unpacks a zip into the install and answers the path of the extracted .txt, refusing before anything is written and undoing itself where a copy fails or is cancelled.
    public static string Extract(string zipPath, string? amkRoot, Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        if (string.IsNullOrEmpty(amkRoot) || !Directory.Exists(amkRoot))
        {
            throw SongZipException.NoAmkFolder();
        }

        string musicFolder = RequiredInstallFolder(amkRoot, AmkInstallFolders.Music);
        string samplesFolder = RequiredInstallFolder(amkRoot, AmkInstallFolders.Samples);
        using var zip = OpenArchive(zipPath);
        var rootFiles = new List<ZipArchiveEntry>();
        var entriesByFolder = new Dictionary<string, List<ParsedEntry>>(StringComparer.OrdinalIgnoreCase);
        var textFiles = new List<ParsedEntry>();
        foreach (var entry in zip.Entries)
        {
            if (ParsedEntryOf(entry) is not { } parsed)
            {
                continue;
            }

            if (FileExtension.Matches(parsed.RelativePath, MmlImporter.Extension))
            {
                textFiles.Add(parsed);
                continue;
            }

            if (parsed.Folder is null)
            {
                rootFiles.Add(entry);
                continue;
            }

            if (!entriesByFolder.TryGetValue(parsed.Folder, out var folderEntries))
            {
                entriesByFolder[parsed.Folder] = folderEntries = [];
            }

            folderEntries.Add(parsed);
        }

        var sortedTextFiles = SortTextFiles(textFiles);
        var mml = RootMml(sortedTextFiles.RootSongs, sortedTextFiles.HasSongInFolder);
        var sampleFolders = SampleFolders(rootFiles, entriesByFolder);
        string mmlDestination = Path.Combine(musicFolder, Path.GetFileName(mml.FullName));
        if (File.Exists(mmlDestination))
        {
            throw SongZipException.SongAlreadyInMusicFolder(Path.GetFileName(mmlDestination));
        }

        foreach (var (folder, _) in sampleFolders)
        {
            string destination = Path.Combine(samplesFolder, folder);
            if (Directory.Exists(destination) || File.Exists(destination))
            {
                throw SongZipException.SampleFolderAlreadyPresent(folder);
            }
        }

        var groupsFileAddition = AdditionToGroupsFile(amkRoot, samplesFolder, sortedTextFiles.Groups, sampleFolders);
        CopyIntoInstall(sampleFolders, samplesFolder, groupsFileAddition, mml, mmlDestination, progress, cancel);
        return mmlDestination;
    }

    // The zip's .txt files sorted into the songs in its root, whether a folder holds a song, and the groups its sample groups files define, any other .txt being left in the zip.
    private static SortedTextFiles SortTextFiles(List<ParsedEntry> textFiles)
    {
        var rootSongs = new List<ZipArchiveEntry>();
        var groups = new List<SampleGroup>();
        bool hasSongInFolder = false;
        foreach (var textFile in textFiles)
        {
            string text = ReadText(textFile.Source);
            if (SampleGroups.GroupsOfSharedFile(text) is { } fileGroups)
            {
                groups.AddRange(fileGroups);
                continue;
            }

            if (!MmlParser.IsSongText(text))
            {
                continue;
            }

            if (textFile.Folder is null)
            {
                rootSongs.Add(textFile.Source);
                continue;
            }

            hasSongInFolder = true;
        }

        return new SortedTextFiles(rootSongs, hasSongInFolder, groups);
    }

    // The text of one file in the archive, refusing an entry that cannot be read.
    private static string ReadText(ZipArchiveEntry entry)
    {
        try
        {
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            throw SongZipException.ExtractFailed(failure.Message);
        }
    }

    // The one song in the zip's root, refusing a zip with none there or more than one.
    private static ZipArchiveEntry RootMml(List<ZipArchiveEntry> rootSongs, bool hasSongInFolder)
    {
        if (rootSongs.Count == 0)
        {
            throw hasSongInFolder ? SongZipException.MmlNotInRoot() : SongZipException.NoMml();
        }

        if (rootSongs.Count > 1)
        {
            throw SongZipException.SeveralMmls(rootSongs.Select(entry => entry.FullName));
        }

        return rootSongs[0];
    }

    // What the zip's groups add to the install's groups file, or null where they add nothing, refusing a group the zip defines twice differently, one
    // the install defines differently, and one naming a sample neither the zip nor the install holds.
    private static GroupsFileAddition? AdditionToGroupsFile(
        string amkRoot, string samplesFolder, List<SampleGroup> zipGroups, List<KeyValuePair<string, List<ParsedEntry>>> sampleFolders)
    {
        if (zipGroups.Count == 0)
        {
            return null;
        }

        string groupsFilePath = SampleGroups.FilePath(amkRoot);
        string? fileText = ReadGroupsFile(amkRoot);
        var installGroups = SampleGroups.ParseGroups(fileText ?? "");
        var unpackedSamplePaths = new HashSet<string>(
            sampleFolders.SelectMany(folder => folder.Value.Select(entry => $"{folder.Key}/{entry.RelativePath}")), StringComparer.OrdinalIgnoreCase);
        var newGroups = new List<SampleGroup>();
        foreach (var group in zipGroups)
        {
            if (installGroups.FirstOrDefault(installed => installed.Name == group.Name) is { } installedGroup)
            {
                if (!installedGroup.HasSameEntriesAs(group))
                {
                    throw SongZipException.GroupDiffersFromInstall(group.Name);
                }

                continue;
            }

            if (newGroups.FirstOrDefault(earlier => earlier.Name == group.Name) is { } earlierGroup)
            {
                if (!earlierGroup.HasSameEntriesAs(group))
                {
                    throw SongZipException.GroupDefinedTwice(group.Name);
                }

                continue;
            }

            RefuseMissingGroupSample(group, unpackedSamplePaths, samplesFolder);
            newGroups.Add(group);
        }

        if (newGroups.Count == 0)
        {
            return null;
        }

        long? groupsFileLengthBeforeAdding = fileText is null ? null : new FileInfo(groupsFilePath).Length;
        return new GroupsFileAddition(groupsFilePath, groupsFileLengthBeforeAdding, SampleGroups.TextToAppend(fileText ?? "", newGroups));
    }

    // The install's groups file's text, or null where it has none, refusing a file that cannot be read.
    private static string? ReadGroupsFile(string amkRoot)
    {
        try
        {
            return SampleGroups.ReadFileText(amkRoot);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw SongZipException.ExtractFailed(failure.Message);
        }
    }

    // Refuses a group naming a sample that is neither among the files the zip unpacks nor already in the samples folder.
    private static void RefuseMissingGroupSample(SampleGroup group, HashSet<string> unpackedPaths, string samplesFolder)
    {
        foreach (var entry in group.Entries)
        {
            if (unpackedPaths.Contains(entry.Path.Replace('\\', '/')) || File.Exists(Path.Combine(samplesFolder, entry.Path)))
            {
                continue;
            }

            throw SongZipException.GroupSampleMissing(group.Name, entry.Path);
        }
    }

    // The root folders that hold a .brr, refusing a .brr outside a folder, a zip with no such folder and anything in one that is not a .brr.
    private static List<KeyValuePair<string, List<ParsedEntry>>> SampleFolders(
        List<ZipArchiveEntry> rootFiles, Dictionary<string, List<ParsedEntry>> entriesByFolder)
    {
        var looseRootSamples = rootFiles.Where(entry => FileExtension.Matches(entry.FullName, Sample.BrrExtension)).ToList();
        if (looseRootSamples.Count > 0)
        {
            throw SongZipException.LooseSamples(looseRootSamples.Select(entry => entry.FullName));
        }

        var sampleFolders = entriesByFolder.Where(pair => pair.Value.Any(entry => FileExtension.Matches(entry.RelativePath, Sample.BrrExtension))).ToList();
        if (sampleFolders.Count == 0)
        {
            throw SongZipException.NoSampleFolder();
        }

        foreach (var (folder, folderEntries) in sampleFolders)
        {
            var nonSampleFiles = folderEntries.Where(entry => !FileExtension.Matches(entry.RelativePath, Sample.BrrExtension)).ToList();
            if (nonSampleFiles.Count > 0)
            {
                throw SongZipException.StrayFileInSampleFolder(folder, nonSampleFiles[0].Source.FullName);
            }
        }

        return sampleFolders;
    }

    // Copies the samples, then adds the new groups and then copies the song, taking away every folder it made and putting the groups file back where a copy fails or is cancelled.
    private static void CopyIntoInstall(
        List<KeyValuePair<string, List<ParsedEntry>>> sampleFolders, string samplesFolder, GroupsFileAddition? groupsFileAddition, ZipArchiveEntry mml,
        string mmlDestination, Action<int, int>? progress, CancellationToken cancel)
    {
        var createdFolders = new List<string>();
        int filesToWrite = sampleFolders.Sum(folder => folder.Value.Count) + (groupsFileAddition is null ? 1 : 2);
        int filesWritten = 0;
        bool isGroupsFileTouched = false;
        progress?.Invoke(0, filesToWrite);
        try
        {
            foreach (var (folder, folderEntries) in sampleFolders)
            {
                string folderPath = Path.Combine(samplesFolder, folder);
                Directory.CreateDirectory(folderPath);
                createdFolders.Add(folderPath);
                foreach (var entry in folderEntries)
                {
                    cancel.ThrowIfCancellationRequested();
                    string destination = Path.Combine(folderPath, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.Source.ExtractToFile(destination);
                    progress?.Invoke(++filesWritten, filesToWrite);
                }
            }

            if (groupsFileAddition is not null)
            {
                cancel.ThrowIfCancellationRequested();
                isGroupsFileTouched = true;
                File.AppendAllText(groupsFileAddition.FilePath, groupsFileAddition.AppendedText);
                progress?.Invoke(++filesWritten, filesToWrite);
            }

            cancel.ThrowIfCancellationRequested();
            mml.ExtractToFile(mmlDestination);
            progress?.Invoke(++filesWritten, filesToWrite);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            DeleteFoldersQuietly(createdFolders);
            if (isGroupsFileTouched)
            {
                RestoreGroupsFileQuietly(groupsFileAddition!);
            }

            if (failure is OperationCanceledException)
            {
                throw;
            }

            throw SongZipException.ExtractFailed(failure.Message);
        }
    }

    // Deletes each folder with everything in it, going on past any that cannot be deleted.
    private static void DeleteFoldersQuietly(List<string> folders)
    {
        foreach (string folder in folders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception)
            {
            }
        }
    }

    // Puts the groups file back as it was before the groups were added, deleting it where there was none, and going on where it cannot be.
    private static void RestoreGroupsFileQuietly(GroupsFileAddition groupsFileAddition)
    {
        try
        {
            if (groupsFileAddition.OriginalLength is not { } originalLength)
            {
                File.Delete(groupsFileAddition.FilePath);
                return;
            }

            using var file = new FileStream(groupsFileAddition.FilePath, FileMode.Open, FileAccess.Write);
            file.SetLength(originalLength);
        }
        catch (Exception)
        {
        }
    }

    // An entry's name split into its root folder and its path under that, or null for a folder entry and for a name no safe destination can be built from.
    private static ParsedEntry? ParsedEntryOf(ZipArchiveEntry entry)
    {
        string name = entry.FullName.Replace('\\', '/');
        if (name.EndsWith('/') || entry.Name.Length == 0)
        {
            return null;
        }

        if (Path.IsPathRooted(name) || name.Contains(':'))
        {
            return null;
        }

        var segments = name.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment == FolderPaths.SameFolder || segment == FolderPaths.ParentFolder))
        {
            return null;
        }

        if (segments.Length == 1)
        {
            return new ParsedEntry(entry, null, segments[0]);
        }

        return new ParsedEntry(entry, segments[0], string.Join('/', segments.Skip(1)));
    }

    // The archive, refusing a file that is not one.
    private static ZipArchive OpenArchive(string zipPath)
    {
        try
        {
            return ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException)
        {
            throw SongZipException.NotZip(Path.GetFileName(zipPath));
        }
    }

    // A folder inside the install, refusing an install that has no such folder.
    private static string RequiredInstallFolder(string root, string name)
    {
        string folder = Path.Combine(root, name);
        if (!Directory.Exists(folder))
        {
            throw SongZipException.InstallFolderMissing(name, folder);
        }

        return folder;
    }

    // An entry of the archive with the root folder it is in, null for one in the root itself, and its path under that folder.
    private readonly record struct ParsedEntry(ZipArchiveEntry Source, string? Folder, string RelativePath);

    // The zip's .txt files once sorted: the songs in its root, whether a folder holds a song, and every group its sample groups files define.
    private sealed record SortedTextFiles(List<ZipArchiveEntry> RootSongs, bool HasSongInFolder, List<SampleGroup> Groups);

    // The text added to the end of the install's groups file, with the file's length before it, null where there was no file.
    private sealed record GroupsFileAddition(string FilePath, long? OriginalLength, string AppendedText);
}
