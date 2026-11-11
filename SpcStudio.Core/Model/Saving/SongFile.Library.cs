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
            var instrumentContents = new SongInstrumentDto { Id = instrument.Id, Name = instrument.Name, SampleId = instrument.SampleId, SmwNumber = instrument.SmwNumber };
            instrumentContents.CopyFrom(instrument);
            contents.Instruments.Add(instrumentContents);
        }
    }

    // Reads the samples and instruments back, refusing two samples under one name and an instrument whose sample is neither in the file nor the sample
    // group's.
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
            if (instrumentContents.SmwNumber is int smwNumber)
            {
                instruments.Add(SmwInstrumentOf(instrumentContents, smwNumber, where));
                continue;
            }

            bool isSampleOutsideFile = instrumentContents.SampleId == Sample.NoiseId || SmwInstruments.IsSampleId(instrumentContents.SampleId);
            if (!isSampleOutsideFile && !samples.Any(sample => sample.Id == instrumentContents.SampleId))
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

    // An SMW instrument read back: the driver's own bytes for its number and the file's other values under the file's id and color, refused where the
    // number is not one SMW plays.
    private static Instrument SmwInstrumentOf(SongInstrumentDto instrumentContents, int smwNumber, string where)
    {
        if (!MmlBuiltins.IsBuiltin(smwNumber))
        {
            throw new InvalidDataException($"{where} is SMW instrument @{smwNumber}, which SMW does not have.");
        }

        var instrument = SmwInstruments.Create(instrumentContents.Id, smwNumber, instrumentContents.Color);
        var driverBytes = instrument.Bytes;
        instrumentContents.CopyTo(instrument, where);
        instrument.Bytes = driverBytes;
        return instrument;
    }

    // A sample as the song file writes it: the shared layout under the id the song's instruments name it by.
    private sealed class SongSampleDto : SampleDto
    {
        public int Id { get; set; }
    }

    // An instrument as the song file writes it: the shared layout under the id the song's notes name it by, its name, the id of the sample it plays, and
    // which SMW instrument it is, if any.
    private sealed class SongInstrumentDto : InstrumentDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int SampleId { get; set; }
        public int? SmwNumber { get; set; }
    }
}
