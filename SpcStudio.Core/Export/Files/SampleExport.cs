using System.IO;
using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Plans and writes a song's samples where AddmusicK will read them for a song in a given folder.
public static class SampleExport
{
    // What writing the song's samples would do, one entry per file, reading the disk and writing nothing.
    public static List<SampleWrite> PlanWrites(string amkRoot, string songFolder, Song song)
    {
        var writes = new List<SampleWrite>();

        foreach (var sample in UsedSamples(song))
        {
            string path = AmkSampleLookup.PathOf(amkRoot, songFolder, Path.Combine(song.SamplePath, sample.FileName));

            writes.Add(new SampleWrite(sample, path, HoldsOtherBytes(path, sample.FileBytes)));
        }

        return writes;
    }

    // Writes every planned file, creating its folder where it is missing, and answers the failure to report or null where all of it worked.
    public static string? WritePlanned(IEnumerable<SampleWrite> plannedWrites)
    {
        foreach (var write in plannedWrites)
        {
            string folder = Path.GetDirectoryName(write.Path)!;
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception exception)
            {
                return $"{folder} could not be created - {exception.Message}";
            }

            try
            {
                File.WriteAllBytes(write.Path, write.Sample.FileBytes);
            }
            catch (Exception exception)
            {
                return $"{write.Sample.FileName} could not be written - {exception.Message}";
            }
        }

        return null;
    }

    // The distinct samples the song's instruments play, by file name, in the order they are first named.
    public static List<Sample> UsedSamples(Song song)
    {
        return UsedSamples(song, MmlExporter.UsedInstruments(song));
    }

    // The distinct samples the given instruments of the song play, by file name, in the order they are first named.
    public static List<Sample> UsedSamples(Song song, IEnumerable<Instrument> usedInstruments)
    {
        var samples = new List<Sample>();
        var seenFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instrument in usedInstruments)
        {
            if (song.SampleFor(instrument) is not { } sample)
            {
                continue;
            }

            if (!seenFileNames.Add(sample.FileName))
            {
                continue;
            }

            samples.Add(sample);
        }

        return samples;
    }

    // Whether a file is already at the path holding other bytes than these, a file that cannot be read counting as one.
    internal static bool HoldsOtherBytes(string path, byte[] fileBytes)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        return ReadableFiles.BytesOrNull(path) is not { } existingBytes || !existingBytes.AsSpan().SequenceEqual(fileBytes);
    }
}
