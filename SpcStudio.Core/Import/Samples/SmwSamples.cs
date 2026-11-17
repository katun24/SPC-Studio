using System.IO;
using SpcStudio.Audio;
using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

// The samples SMW's instruments play: those a sample group holds at each position, each under the id its position gives it.
public static class SmwSamples
{
    // The samples a group holds, read out of an install's samples folder, a position whose file is missing, empty or not a sample holding none.
    public static List<Sample> Read(string amkRoot, IReadOnlyList<string> groupPaths)
    {
        return [.. Enumerable.Range(0, groupPaths.Count).Select(position => ReadAt(amkRoot, groupPaths, position)).OfType<Sample>()];
    }

    // The ids of the samples a group's files hold by their sizes alone, a position whose file is missing or empty holding none, no file being read.
    public static HashSet<int> IdsHeldByFileSize(string amkRoot, IReadOnlyList<string> groupPaths)
    {
        return [.. Enumerable.Range(0, groupPaths.Count).Where(position => IsFileFilled(AmkSampleLookup.InSamplesFolder(amkRoot, groupPaths[position]))).Select(SmwInstruments.SampleIdOf)];
    }

    // The sample a group holds at a position, read out of an install's samples folder, or null where the position is past the group or holds none.
    public static Sample? ReadAt(string amkRoot, IReadOnlyList<string> groupPaths, int position)
    {
        if (position >= groupPaths.Count || ReadableFiles.BytesOrNull(AmkSampleLookup.InSamplesFolder(amkRoot, groupPaths[position])) is not { } fileBytes)
        {
            return null;
        }

        return FromBytes(position, groupPaths[position], fileBytes);
    }

    // The sample a group's file holds at a position, or null where the file is empty or not a sample.
    public static Sample? FromBytes(int position, string groupPath, byte[] fileBytes)
    {
        if (IsPlaceholderLength(fileBytes.Length))
        {
            return null;
        }

        try
        {
            return Sample.FromBytes(Path.GetFileName(groupPath), fileBytes) with { Id = SmwInstruments.SampleIdOf(position), PathInSampleGroup = groupPath };
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    // Whether a file is there and is longer than AddmusicK's empty placeholder.
    private static bool IsFileFilled(string path)
    {
        var file = new FileInfo(path);
        return file.Exists && !IsPlaceholderLength(file.Length);
    }

    // Whether a file of a length is AddmusicK's empty placeholder, which holds no sample.
    private static bool IsPlaceholderLength(long fileLength)
    {
        return fileLength == 0;
    }
}
