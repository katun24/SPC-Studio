using System.IO;
using System.IO.Compression;
using SpcStudio.Audio;
using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

// Unpacks a shared song - one .txt, the folders of .brr samples it plays and any sample groups it brings - into an AddmusicK install, checking the
// whole archive before writing a byte and overwriting nothing but the samples it was allowed to.
public static class SongZip
{
    public const string Extension = ".zip";
    public const string MmlOrZipDialogFilter = "AddmusicK MML or zip (*.txt;*.zip)|*.txt;*.zip" + DialogFilters.Separator + DialogFilters.AllFiles;
    private const char ArchivePathSeparator = '/';
    private const char BackslashPathSeparator = '\\';
    private const string MacFileInformationFolder = "__MACOSX";
    private const string MacFileInformationPrefix = "._";

    // Whether a picked file is a zip rather than a .txt.
    public static bool IsZip(string path)
    {
        return FileExtension.Matches(path, Extension);
    }

    // Unpacks a zip into the install and answers the path of the extracted .txt, refusing before anything is written, asking which song to unpack where it
    // holds more than one, asking before the song or the samples already there with other bytes are overwritten, stopping as cancelled where no song is
    // chosen or a file may not be overwritten, and undoing itself where a copy fails or is cancelled.
    public static string Extract(string zipPath, string? amkRoot, Func<IReadOnlyList<string>, int?> chooseSong, InstallOverwriteConsent overwriteConsent,
        Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        if (string.IsNullOrEmpty(amkRoot) || !Directory.Exists(amkRoot))
        {
            throw SongZipException.NoAmkFolder();
        }

        string musicFolder = RequiredInstallFolder(amkRoot, AmkInstallFolders.Music);
        string samplesFolder = RequiredInstallFolder(amkRoot, AmkInstallFolders.Samples);
        using var zip = OpenArchive(zipPath);
        var textFiles = new List<ArchiveFile>();
        var otherFiles = new List<ArchiveFile>();
        foreach (var entry in zip.Entries)
        {
            if (ArchiveFileOf(entry) is not { } file)
            {
                continue;
            }

            if (IsMacFileInformation(file))
            {
                continue;
            }

            if (FileExtension.Matches(entry.FullName, MmlImporter.Extension))
            {
                textFiles.Add(file);
                continue;
            }

            otherFiles.Add(file);
        }

        var sortedTextFiles = SortTextFiles(textFiles);
        var mml = ChosenSong(sortedTextFiles.Songs, chooseSong);
        var sampleFoldersParent = ContentsOf(otherFiles, SampleFoldersParent(mml.Segments, otherFiles));
        var (sampleFolders, looseSampleCopies) = SamplePlacesOf(sampleFoldersParent, mml, amkRoot, samplesFolder);
        string mmlDestination = Path.Combine(musicFolder, mml.Segments[^1]);
        foreach (var (folder, _) in sampleFolders)
        {
            if (File.Exists(Path.Combine(samplesFolder, folder)))
            {
                throw SongZipException.AlreadyInSamplesFolder(folder);
            }
        }

        var sampleCopies = SampleFolderCopies(sampleFolders, samplesFolder).Concat(looseSampleCopies).ToList();
        var groupsFileAddition = AdditionToGroupsFile(amkRoot, samplesFolder, sortedTextFiles.Groups, UnpackedSamplePaths(sampleCopies));
        var mmlCopy = FileCopyUnlessAlreadyThere(mml.Source, mmlDestination);
        overwriteConsent.AskOrCancel(mmlCopy is { IsOverwriting: true } ? mml.Segments[^1] : null,
            sampleCopies.Where(sampleCopy => sampleCopy.Copy.IsOverwriting).Select(sampleCopy => sampleCopy.NameUnderSamplesFolder).ToList());
        CopyIntoInstall(sampleCopies.Select(sampleCopy => sampleCopy.Copy).ToList(), groupsFileAddition, mmlCopy, progress, cancel);
        return mmlDestination;
    }

    // Whether a file is the information a Mac keeps about another file rather than a file of the song: anything in the folder a Mac's own zip puts
    // that information in, and any file named as such a copy wherever it stands.
    private static bool IsMacFileInformation(ArchiveFile file)
    {
        return file.Segments[0] == MacFileInformationFolder || file.Segments[^1].StartsWith(MacFileInformationPrefix, StringComparison.Ordinal);
    }

    // The zip's .txt files sorted into its songs, wherever they stand, and the groups its sample groups files define, any other .txt being left in the zip.
    private static SortedTextFiles SortTextFiles(List<ArchiveFile> textFiles)
    {
        var songs = new List<ArchiveFile>();
        var groups = new List<SampleGroup>();
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

            songs.Add(textFile);
        }

        return new SortedTextFiles(songs, groups);
    }

    // The text of one file in the archive, refusing an entry that cannot be read.
    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(new MemoryStream(ReadBytes(entry)));
        return reader.ReadToEnd();
    }

    // The bytes of one file in the archive, refusing an entry that cannot be read.
    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        try
        {
            using var entryStream = entry.Open();
            using var bytes = new MemoryStream();
            entryStream.CopyTo(bytes);
            return bytes.ToArray();
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            throw SongZipException.ExtractFailed(failure.Message);
        }
    }

    // The zip's one song, or where it holds more than one the song chosen from their shown names in name order, refusing a zip with none and stopping as
    // cancelled where none is chosen.
    private static ArchiveFile ChosenSong(List<ArchiveFile> songs, Func<IReadOnlyList<string>, int?> chooseSong)
    {
        if (songs.Count == 0)
        {
            throw SongZipException.NoMml();
        }

        if (songs.Count == 1)
        {
            return songs[0];
        }

        var songsInNameOrder = songs.Select(song => (ShownName: ShownNameOf(song, songs), Song: song)).OrderBy(shown => shown.ShownName, NameOrder.Instance).ToList();
        if (chooseSong(songsInNameOrder.Select(shown => shown.ShownName).ToList()) is not { } chosenIndex)
        {
            throw new OperationCanceledException();
        }

        return songsInNameOrder[chosenIndex].Song;
    }

    // A song's name as the choice shows it: its file name, or its whole path in the zip where another song has the same file name.
    private static string ShownNameOf(ArchiveFile song, List<ArchiveFile> songs)
    {
        bool isFileNameShared = songs.Count(other => string.Equals(other.Segments[^1], song.Segments[^1], StringComparison.OrdinalIgnoreCase)) > 1;
        return isFileNameShared ? string.Join(ArchivePathSeparator, song.Segments) : song.Segments[^1];
    }

    // The folder the song's sample folders stand in: the samples folder beside its music folder where the zip is laid out as an AddmusicK install, and the
    // song's own folder otherwise.
    private static string[] SampleFoldersParent(string[] songSegments, List<ArchiveFile> files)
    {
        string[] songFolder = songSegments[..^1];
        if (songFolder.Length == 0 || !string.Equals(songFolder[^1], AmkInstallFolders.Music, StringComparison.OrdinalIgnoreCase))
        {
            return songFolder;
        }

        string[] installSamplesFolder = [.. songFolder[..^1], AmkInstallFolders.Samples];
        return files.Any(file => IsInside(file, installSamplesFolder)) ? installSamplesFolder : songFolder;
    }

    // Whether a file stands somewhere inside a folder of the archive.
    private static bool IsInside(ArchiveFile file, string[] folderSegments)
    {
        return file.Segments.Length > folderSegments.Length && file.Segments.Take(folderSegments.Length).SequenceEqual(folderSegments, StringComparer.OrdinalIgnoreCase);
    }

    // The files inside a folder of the archive, split into those standing in it directly and those in each of its subfolders, everything elsewhere left out.
    private static FolderContents ContentsOf(List<ArchiveFile> files, string[] folderSegments)
    {
        var looseFiles = new List<ArchiveFile>();
        var entriesBySubfolder = new Dictionary<string, List<ParsedEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(file => IsInside(file, folderSegments)))
        {
            string[] relativeSegments = file.Segments[folderSegments.Length..];
            if (relativeSegments.Length == 1)
            {
                looseFiles.Add(file);
                continue;
            }

            if (!entriesBySubfolder.TryGetValue(relativeSegments[0], out var subfolderEntries))
            {
                entriesBySubfolder[relativeSegments[0]] = subfolderEntries = [];
            }

            subfolderEntries.Add(new ParsedEntry(file.Source, string.Join(ArchivePathSeparator, relativeSegments[1..])));
        }

        return new FolderContents(looseFiles, entriesBySubfolder);
    }

    // The sample folders unpacked as folders, and each sample unpacked loose with where it goes: a .brr standing beside the sample folders, and every
    // file of a folder the song names its samples without where none of them lands on a place another of the zip's files fills, the song's sample list
    // read only where the zip holds a sample.
    private static SamplePlaces SamplePlacesOf(FolderContents sampleFoldersParent, ArchiveFile mml, string amkRoot, string samplesFolder)
    {
        var sampleFolders = SampleFolders(sampleFoldersParent);
        var looseSamples = sampleFoldersParent.LooseFiles
            .Where(file => IsSample(file.Source.FullName))
            .Select(file => new LooseSample(file.Source, file.Segments[^1]))
            .ToList();
        if (sampleFolders.Count == 0 && looseSamples.Count == 0)
        {
            return new SamplePlaces(sampleFolders, []);
        }

        var songSamples = MmlSampleListReader.Read(ReadText(mml.Source));
        var foldersByIsNamedWithout = sampleFolders.ToLookup(folder => IsNamedWithoutItsFolder(folder, songSamples, amkRoot));
        var keptFolders = foldersByIsNamedWithout[false].ToList();
        var filledPlaces = FilledPlaces(keptFolders, looseSamples, songSamples.SamplePath, amkRoot, samplesFolder);
        foreach (var folder in foldersByIsNamedWithout[true])
        {
            var placesUnderSongPath = folder.Value.Select(entry => Path.GetFullPath(PlaceUnderSongPath(amkRoot, songSamples.SamplePath, entry.RelativePath))).ToList();
            if (placesUnderSongPath.Any(filledPlaces.Contains))
            {
                keptFolders.Add(folder);
                continue;
            }

            filledPlaces.UnionWith(placesUnderSongPath);
            looseSamples.AddRange(folder.Value.Select(entry => new LooseSample(entry.Source, entry.RelativePath)));
        }

        return new SamplePlaces(keptFolders, LooseSampleCopies(looseSamples, songSamples.SamplePath, amkRoot, samplesFolder));
    }

    // Every place in the install that the folders unpacked as folders and the samples standing loose beside them fill, each as a full path.
    private static HashSet<string> FilledPlaces(List<KeyValuePair<string, List<ParsedEntry>>> keptFolders, List<LooseSample> looseSamples, string songSamplePath,
        string amkRoot, string samplesFolder)
    {
        var folderFilePlaces = keptFolders.SelectMany(folder => folder.Value.Select(entry => FolderFilePlace(samplesFolder, folder.Key, entry.RelativePath)));
        var looseSamplePlaces = looseSamples.Select(looseSample => PlaceUnderSongPath(amkRoot, songSamplePath, looseSample.Name));
        return new HashSet<string>(folderFilePlaces.Concat(looseSamplePlaces).Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
    }

    // Where a sample goes under the song's #path in the install's samples folder.
    private static string PlaceUnderSongPath(string amkRoot, string songSamplePath, string sampleName)
    {
        return AmkSampleLookup.InSamplesFolder(amkRoot, Path.Combine(songSamplePath, sampleName));
    }

    // Where a file of a sample folder unpacked as a folder goes in the install's samples folder.
    private static string FolderFilePlace(string samplesFolder, string folder, string relativePath)
    {
        return Path.Combine(samplesFolder, folder, relativePath.Replace(ArchivePathSeparator, Path.DirectorySeparatorChar));
    }

    // Whether the song names every sample of a folder by its path inside the folder, and so looks for none of them in the folder's own place in the
    // samples folder.
    private static bool IsNamedWithoutItsFolder(KeyValuePair<string, List<ParsedEntry>> folder, MmlSampleList songSamples, string amkRoot)
    {
        var namedFiles = new HashSet<string>(MmlSampleEntry.FileNamesIn(songSamples.Samples), StringComparer.OrdinalIgnoreCase);
        string folderPlace = AmkSampleLookup.InSamplesFolder(amkRoot, folder.Key);
        return folder.Value.All(entry => namedFiles.Contains(entry.RelativePath)
            && FolderPaths.RelativePathUnder(folderPlace, PlaceUnderSongPath(amkRoot, songSamples.SamplePath, entry.RelativePath)) is null);
    }

    // Each loose sample's copy under the song's #path in the samples folder, leaving out one whose place would fall outside the samples folder.
    private static List<SampleCopy> LooseSampleCopies(List<LooseSample> looseSamples, string songSamplePath, string amkRoot, string samplesFolder)
    {
        var copies = new List<SampleCopy>();
        foreach (var looseSample in looseSamples)
        {
            string nameUnderSamplesFolder = Path.Combine(songSamplePath, looseSample.Name);
            string destination = PlaceUnderSongPath(amkRoot, songSamplePath, looseSample.Name);
            if (FolderPaths.RelativePathUnder(samplesFolder, destination) is null)
            {
                continue;
            }

            if (SampleCopyUnlessAlreadyThere(looseSample.Source, destination, nameUnderSamplesFolder) is { } copy)
            {
                copies.Add(copy);
            }
        }

        return copies;
    }

    // Each sample folder's files' copies under the samples folder.
    private static IEnumerable<SampleCopy> SampleFolderCopies(List<KeyValuePair<string, List<ParsedEntry>>> sampleFolders, string samplesFolder)
    {
        foreach (var (folder, entries) in sampleFolders)
        {
            foreach (var entry in entries)
            {
                string destination = FolderFilePlace(samplesFolder, folder, entry.RelativePath);
                if (SampleCopyUnlessAlreadyThere(entry.Source, destination, folder + ArchivePathSeparator + entry.RelativePath) is { } copy)
                {
                    yield return copy;
                }
            }
        }
    }

    // A sample's copy to its place, by its path under the samples folder, null where the place already holds the same bytes.
    private static SampleCopy? SampleCopyUnlessAlreadyThere(ZipArchiveEntry source, string destination, string nameUnderSamplesFolder)
    {
        if (FileCopyUnlessAlreadyThere(source, destination) is not { } copy)
        {
            return null;
        }

        return new SampleCopy(copy, nameUnderSamplesFolder.Replace(BackslashPathSeparator, ArchivePathSeparator));
    }

    // A file's copy to its place, null where the place already holds the same bytes, and marked as overwriting where it holds others.
    private static FileCopy? FileCopyUnlessAlreadyThere(ZipArchiveEntry source, string destination)
    {
        return InstallCopyPlan.UnlessAlreadyThere(destination, ReadBytes(source), isOverwriting => new FileCopy(source, destination, isOverwriting));
    }

    // Every sample the zip unpacks by its path under the samples folder, spelled with the archive's separator as a group's entry is compared.
    private static HashSet<string> UnpackedSamplePaths(List<SampleCopy> sampleCopies)
    {
        return new HashSet<string>(sampleCopies.Select(sampleCopy => sampleCopy.NameUnderSamplesFolder), StringComparer.OrdinalIgnoreCase);
    }

    // What the zip's groups add to the install's groups file, or null where they add nothing, refusing a group the zip defines twice differently, one
    // the install defines differently, and one naming a sample neither the zip nor the install holds.
    private static GroupsFileAddition? AdditionToGroupsFile(string amkRoot, string samplesFolder, List<SampleGroup> zipGroups, HashSet<string> unpackedSamplePaths)
    {
        if (zipGroups.Count == 0)
        {
            return null;
        }

        string groupsFilePath = SampleGroups.FilePath(amkRoot);
        string? fileText = ReadGroupsFile(amkRoot);
        var installGroups = SampleGroups.ParseGroups(fileText ?? "");
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
            if (unpackedPaths.Contains(entry.Path.Replace(BackslashPathSeparator, ArchivePathSeparator)) || File.Exists(Path.Combine(samplesFolder, entry.Path)))
            {
                continue;
            }

            throw SongZipException.GroupSampleMissing(group.Name, entry.Path);
        }
    }

    // The subfolders of the folder the sample folders stand in that hold a .brr, each with its samples alone, none where the song brings no sample folders.
    private static List<KeyValuePair<string, List<ParsedEntry>>> SampleFolders(FolderContents sampleFoldersParent)
    {
        return sampleFoldersParent.EntriesBySubfolder
            .Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Where(entry => IsSample(entry.RelativePath)).ToList()))
            .Where(pair => pair.Value.Count > 0)
            .ToList();
    }

    // Whether a file of the zip is a sample, by its extension.
    private static bool IsSample(string path)
    {
        return FileExtension.Matches(path, Sample.BrrExtension);
    }

    // Copies the samples, then adds the new groups and then copies the song where the music folder does not already hold it, taking back every file and
    // folder it made and putting the files it overwrote and the groups file back where a copy fails or is cancelled.
    private static void CopyIntoInstall(List<FileCopy> sampleCopies, GroupsFileAddition? groupsFileAddition, FileCopy? mmlCopy, Action<int, int>? progress, CancellationToken cancel)
    {
        var rollback = new InstallCopyRollback();
        int filesToWrite = sampleCopies.Count + (groupsFileAddition is null ? 0 : 1) + (mmlCopy is null ? 0 : 1);
        int filesWritten = 0;
        bool isGroupsFileTouched = false;
        progress?.Invoke(0, filesToWrite);
        try
        {
            foreach (var sampleCopy in sampleCopies)
            {
                cancel.ThrowIfCancellationRequested();
                ExtractNoted(sampleCopy, rollback);
                progress?.Invoke(++filesWritten, filesToWrite);
            }

            if (groupsFileAddition is not null)
            {
                cancel.ThrowIfCancellationRequested();
                isGroupsFileTouched = true;
                File.AppendAllText(groupsFileAddition.FilePath, groupsFileAddition.AppendedText);
                progress?.Invoke(++filesWritten, filesToWrite);
            }

            if (mmlCopy is { } copy)
            {
                cancel.ThrowIfCancellationRequested();
                ExtractNoted(copy, rollback);
                progress?.Invoke(++filesWritten, filesToWrite);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            rollback.TakeBack();
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

    // Writes one file of the archive to its place, creating the folders it needs, all of it noted to be taken back first.
    private static void ExtractNoted(FileCopy copy, InstallCopyRollback rollback)
    {
        rollback.NoteBeforeWriting(copy.Destination);
        copy.Source.ExtractToFile(copy.Destination, copy.IsOverwriting);
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

    // An entry with its name split into its folders and its file name, or null for a folder entry and for a name no safe destination can be built from.
    private static ArchiveFile? ArchiveFileOf(ZipArchiveEntry entry)
    {
        string name = entry.FullName.Replace(BackslashPathSeparator, ArchivePathSeparator);
        if (name.EndsWith(ArchivePathSeparator) || entry.Name.Length == 0)
        {
            return null;
        }

        if (Path.IsPathRooted(name) || name.Contains(':'))
        {
            return null;
        }

        var segments = name.Split(ArchivePathSeparator);
        if (segments.Any(segment => segment.Length == 0 || segment == FolderPaths.SameFolder || segment == FolderPaths.ParentFolder))
        {
            return null;
        }

        return new ArchiveFile(entry, segments);
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

    // An entry of the archive with the folders and the file name its name is made of.
    private readonly record struct ArchiveFile(ZipArchiveEntry Source, string[] Segments);

    // An entry of a sample folder with its path under that folder.
    private readonly record struct ParsedEntry(ZipArchiveEntry Source, string RelativePath);

    // What a folder of the archive holds: the files standing in it directly, and those in each of its subfolders by the subfolder's name.
    private sealed record FolderContents(List<ArchiveFile> LooseFiles, Dictionary<string, List<ParsedEntry>> EntriesBySubfolder);

    // One file of the archive with the place in the install it is written to, and whether it writes over a file already there.
    private readonly record struct FileCopy(ZipArchiveEntry Source, string Destination, bool IsOverwriting = false);

    // A sample unpacked loose, with its path under the song's #path.
    private readonly record struct LooseSample(ZipArchiveEntry Source, string Name);

    // A sample's copy with its path under the samples folder, spelled with the archive's separator.
    private sealed record SampleCopy(FileCopy Copy, string NameUnderSamplesFolder);

    // Where the zip's samples unpack: the sample folders unpacked as folders, and the loose samples' copies.
    private sealed record SamplePlaces(List<KeyValuePair<string, List<ParsedEntry>>> SampleFolders, List<SampleCopy> LooseSampleCopies);

    // The zip's .txt files once sorted: its songs and every group its sample groups files define.
    private sealed record SortedTextFiles(List<ArchiveFile> Songs, List<SampleGroup> Groups);

    // The text added to the end of the install's groups file, with the file's length before it, null where there was no file.
    private sealed record GroupsFileAddition(string FilePath, long? OriginalLength, string AppendedText);
}
