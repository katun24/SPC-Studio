using System.IO;
using SpcStudio.Audio;

namespace SpcStudio.Model;

// One instrument on its own, saved outside any song with the sample it plays.
public sealed class InstrumentFile
{
    public const string Extension = ".spcins";
    public const string DialogFilter = "SPC Studio instrument (*.spcins)|*.spcins" + DialogFilters.Separator + DialogFilters.AllFiles;
    private const string FileKind = "instrument";
    private const int CurrentVersion = 12;
    private const int UnplacedInstrumentId = 0;
    private const int UnplacedSampleId = 0;

    private readonly InstrumentFileDto _contents;

    private InstrumentFile(string name, InstrumentFileDto contents, Sample? sample)
    {
        Name = name;
        _contents = contents;
        Sample = sample;
    }

    // The instrument's name, which is its file's name.
    public string Name { get; }

    // The sample it plays, or null where it plays the noise generator instead.
    public Sample? Sample { get; }

    // Writes one instrument and the sample it plays, which is null only for a noise instrument.
    public static void Save(string path, Instrument instrument, Sample? sample)
    {
        if (sample is null && !instrument.IsNoise)
        {
            throw new InvalidDataException($"'{instrument.Name}' plays a sample that is not in the song.");
        }

        var contents = new InstrumentFileDto { Version = CurrentVersion };
        contents.CopyFrom(instrument);
        if (sample is not null)
        {
            contents.Sample = new SampleDto();
            contents.Sample.CopyFrom(sample);
        }

        SavedFileJson.Write(path, contents);
    }

    // The name of the instrument a file holds: the file's own name, without the extension.
    public static string NameOf(string path)
    {
        return Path.GetFileNameWithoutExtension(path);
    }

    // The path of the file an instrument of a name has in a folder.
    public static string PathFor(string folderPath, string name)
    {
        return Path.Combine(folderPath, FileNameFor(name));
    }

    // The name of the file an instrument of a name is saved in.
    public static string FileNameFor(string name)
    {
        return name + Extension;
    }

    // Reads one back, refusing a file that is not one of ours or holds anything an instrument could not as one that is not a valid instrument file.
    public static InstrumentFile Load(string path)
    {
        return SavedFileJson.ReadRefusingAsNotValid(path, FileKind, ReadInstrumentFile);
    }

    // Reads one back, refusing a value out of its range and a sample that is not a .brr before any song is touched, each refusal saying what is wrong with it.
    private static InstrumentFile ReadInstrumentFile(string path)
    {
        string quotedFileName = SavedFileJson.QuotedFileName(path);
        var contents = SavedFileJson.Read<InstrumentFileDto>(path, FileKind, CurrentVersion);
        var sample = contents.Sample?.ToSample(UnplacedSampleId, quotedFileName);
        string name = NameOf(path);
        var checkedInstrument = new Instrument
        {
            Id = UnplacedInstrumentId,
            Name = name,
            SampleId = sample?.Id ?? Audio.Sample.NoiseId,
            ColorRgb = contents.Color,
        };
        contents.CopyTo(checkedInstrument, quotedFileName);
        return new InstrumentFile(name, contents, sample);
    }

    // Adds this instrument to a song, bringing its sample in with it, and returns it as stored, the song assigning both ids and the name where the file's is taken.
    public Instrument AddTo(Song song)
    {
        string name = song.FreeInstrumentName(Name);

        // The new instrument in the song, on the song's copy of the sample or on the noise generator.
        var instrument = Sample is { } sample ? song.CreateInstrument(name, song.SameSampleAs(sample) ?? song.AddSampleUnderFreeName(sample)) : song.CreateNoiseInstrument(name);
        _contents.CopyTo(instrument, $"'{Name}'");
        instrument.ColorRgb = _contents.Color;
        return instrument;
    }

    // An instrument as the instrument file writes it: the shared layout, the layout's version and the sample it plays.
    private sealed class InstrumentFileDto : InstrumentDto, IVersionedContents
    {
        public int Version { get; set; }
        public SampleDto? Sample { get; set; }
    }
}
