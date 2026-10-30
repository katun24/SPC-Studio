using System.IO;
using SpcStudio.Audio;

namespace SpcStudio.Model;

// The song file's samples and instruments.
public static partial class SongFile
{
    // Writes the song's samples and instruments.
    private static void WriteLibraries(Song song, SongDto contents)
    {
        foreach (var sample in song.Samples)
        {
            var sampleContents = new SongSampleDto { Id = sample.Id };
            sampleContents.CopyFrom(sample);
            contents.Samples.Add(sampleContents);
        }

        foreach (var instrument in song.Instruments)
        {
            var instrumentContents = new SongInstrumentDto { Id = instrument.Id, Name = instrument.Name, SampleId = instrument.SampleId };
            instrumentContents.CopyFrom(instrument);
            contents.Instruments.Add(instrumentContents);
        }
    }

    // Reads the samples and instruments back, refusing two samples under one name and an instrument whose sample is not in the file.
    private static void ReadLibraries(Song song, SongDto contents, string fileName)
    {
        // The file's samples, each read back under its own id.
        var samples = contents.Samples.Select(sample => sample.ToSample(sample.Id, $"{fileName} sample '{sample.FileName}'")).ToList();

        var samplesSharingOneName = samples.GroupBy(sample => sample.FileName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(names => names.Count() > 1);
        if (samplesSharingOneName is not null)
        {
            throw new InvalidDataException($"{fileName} has two samples called '{samplesSharingOneName.First().FileName}'.");
        }

        var instruments = new List<Instrument>();
        foreach (var instrumentContents in contents.Instruments)
        {
            string where = $"{fileName} instrument '{instrumentContents.Name}'";
            if (instrumentContents.SampleId != Sample.NoiseId && !samples.Any(sample => sample.Id == instrumentContents.SampleId))
            {
                throw new InvalidDataException($"{where} uses sample {instrumentContents.SampleId}, which is not in the file.");
            }

            var instrument = new Instrument
            {
                Id = instrumentContents.Id,
                Name = instrumentContents.Name,
                SampleId = instrumentContents.SampleId,
                ColorRgb = instrumentContents.Color,
            };
            instrumentContents.CopyTo(instrument, where);
            instruments.Add(instrument);
        }

        song.SetLibraries(samples, instruments);
    }

    // A sample as the song file writes it: the shared layout under the id the song's instruments name it by.
    private sealed class SongSampleDto : SampleDto
    {
        public int Id { get; set; }
    }

    // An instrument as the song file writes it: the shared layout under the id the song's notes name it by, its name, and the id of the sample it plays.
    private sealed class SongInstrumentDto : InstrumentDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int SampleId { get; set; }
    }
}
