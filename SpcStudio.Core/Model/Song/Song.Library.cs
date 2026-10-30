using SpcStudio.Audio;

namespace SpcStudio.Model;

// The song's samples and the instruments built on them: adding, copying, deleting and naming them.
public sealed partial class Song
{
    private const string NumberedSampleNamePrefix = "Sample";
    private const string NumberedInstrumentNamePrefix = "Instrument";

    private readonly List<Sample> _samples = [];
    private readonly List<Instrument> _instruments = [];

    // The samples in the song, in name order.
    public IReadOnlyList<Sample> Samples
    {
        get
        {
            return [.. _samples.OrderBy(sample => sample.Name, NameOrder.Instance)];
        }
    }

    // The instruments in the song, in name order.
    public IReadOnlyList<Instrument> Instruments
    {
        get
        {
            return [.. _instruments.OrderBy(instrument => instrument.Name, NameOrder.Instance)];
        }
    }

    // The sample added to the song last, or null where it holds none.
    public Sample? NewestSample
    {
        get
        {
            return _samples.LastOrDefault();
        }
    }

    // The sample with an id, or null where the song holds none.
    public Sample? SampleById(int id)
    {
        return _samples.FirstOrDefault(sample => sample.Id == id);
    }

    // The instrument with an id, or null where the song holds none.
    public Instrument? InstrumentById(int id)
    {
        return _instruments.FirstOrDefault(instrument => instrument.Id == id);
    }

    // The instrument a note plays, or null where the song holds none.
    public Instrument? InstrumentOf(Note note)
    {
        return InstrumentById(note.InstrumentId);
    }

    // The sample an instrument plays, or null where it has none.
    public Sample? SampleFor(Instrument instrument)
    {
        return SampleById(instrument.SampleId);
    }

    // How much of its original rate an instrument's sample is at, which its tuning is worked out for: all of it where it has no sample.
    public double RateFractionOf(Instrument instrument)
    {
        return SampleFor(instrument)?.RateFractionOfOriginal ?? Sample.OriginalRateFraction;
    }

    // The tuning an instrument sounds and exports with: its own, worked out for the rate its sample plays at.
    public double SoundingTuningOf(Instrument instrument)
    {
        return instrument.TuningMultiplierAt(RateFractionOf(instrument));
    }

    // Whether a note sounds at all: its instrument is in the song and has a sample, or plays the noise generator.
    public bool IsNotePlayable(Note note)
    {
        return InstrumentOf(note) is { } instrument && (instrument.IsNoise || SampleFor(instrument) is not null);
    }

    // Whether a note plays the noise generator, which is its instrument's to say.
    public bool IsNoiseNote(Note note)
    {
        return InstrumentOf(note) is { IsNoise: true };
    }

    // Whether a sample of this file name is already in the song, ignoring case.
    public bool HasSample(string fileName)
    {
        return SampleNamed(fileName) is not null;
    }

    // The sample of this file name, ignoring case, or null where the song holds none.
    public Sample? SampleNamed(string fileName)
    {
        return _samples.FirstOrDefault(sample => IsSameSampleName(sample.FileName, fileName));
    }

    // The sample in the song holding the same source under the same effects as a sample, under the sample's name or a copy-numbered version of it, or null where none does.
    public Sample? SameSampleAs(Sample sample)
    {
        string stem = FreeNames.StemOf(sample.Name);
        return _samples.FirstOrDefault(songSample => IsSameSampleName(FreeNames.StemOf(songSample.Name), stem) && songSample.PlaysSameAudioAs(sample));
    }

    // Why a sample cannot be renamed to a name - left empty, ending in a dot, or held by another sample - or null where it can.
    public string? SampleNameRefusal(Sample sample, string name)
    {
        if (NameRefusals.OfDiskName(NamedThing.Sample, name) is { } refusal)
        {
            return refusal;
        }

        if (Sample.IsReservedName(name))
        {
            return NameRefusals.Reserved(NamedThing.Sample, name);
        }

        return SampleNamed(sample.WithName(name).FileName) is { } holder && holder.Id != sample.Id ? NameRefusals.TakenInSong(NamedThing.Sample, name) : null;
    }

    // Why an instrument cannot be renamed to a name - left empty, or held by another instrument - or null where it can.
    public string? InstrumentNameRefusal(Instrument instrument, string name)
    {
        if (name.Length == 0)
        {
            return NameRefusals.Empty(NamedThing.Instrument);
        }

        return InstrumentsNamed(name).Any(other => other.Id != instrument.Id) ? NameRefusals.TakenInSong(NamedThing.Instrument, name) : null;
    }

    // Adds a sample under the next free id and answers it as stored.
    public Sample AddSample(Sample sample)
    {
        var storedSample = sample with { Id = NextFreeId(_samples.Select(existing => existing.Id)) };
        _samples.Add(storedSample);
        return storedSample;
    }

    // The first numbered sample name no sample in the song holds.
    public string FreeSampleName()
    {
        return FreeNames.FirstFreeNumberedName(NumberedSampleNamePrefix, Sample.BrrExtension, HasSample);
    }

    // Adds a new silent sample under the next free name and answers it as stored.
    public Sample CreateSample()
    {
        return AddSample(Sample.Empty(FreeSampleName()));
    }

    // Adds a sample under its own name where no sample in the song holds it, and otherwise under the first copy-numbered name none does, answering it as stored.
    public Sample AddSampleUnderFreeName(Sample sample)
    {
        return AddSample(sample with { FileName = FreeNames.FirstFreeName(sample.Name, Path.GetExtension(sample.FileName), IsSampleFileNameUnavailable) });
    }

    // Adds a second sample over the same audio under the first free copy-numbered name and answers it as stored.
    public Sample DuplicateSample(int id)
    {
        return AddSampleUnderFreeName(_samples.First(sample => sample.Id == id));
    }

    // How many instruments are built on a sample.
    public int InstrumentCountUsingSample(int sampleId)
    {
        return _instruments.Count(instrument => instrument.SampleId == sampleId);
    }

    // How many notes, over every channel, play an instrument built on a sample.
    public int NoteCountUsingSample(int sampleId)
    {
        var instrumentIds = _instruments.Where(instrument => instrument.SampleId == sampleId).Select(instrument => instrument.Id).ToHashSet();
        return _notes.Sum(notes => notes.Count(note => instrumentIds.Contains(note.InstrumentId)));
    }

    // Deletes a sample, every instrument built on it, and every note those instruments play.
    public void DeleteSample(int id)
    {
        foreach (int instrumentId in _instruments.Where(instrument => instrument.SampleId == id).Select(instrument => instrument.Id).ToArray())
        {
            DeleteInstrument(instrumentId);
        }

        _samples.RemoveAll(sample => sample.Id == id);
    }

    // Puts an edited sample where the one with its id stood.
    public void ReplaceSample(Sample sample)
    {
        _samples[_samples.FindIndex(existing => existing.Id == sample.Id)] = sample;
    }

    // The preferred instrument name where no instrument holds it, and otherwise the first copy-numbered name of it none holds.
    public string FreeInstrumentName(string preferredName)
    {
        return FreeNames.FirstFreeName(preferredName, "", HasInstrumentNamed);
    }

    // The first numbered instrument name no instrument in the song holds.
    private string FreeNumberedInstrumentName()
    {
        return FreeNames.FirstFreeNumberedName(NumberedInstrumentNamePrefix, "", HasInstrumentNamed);
    }

    // Adds an instrument on a sample, tuned by the sample's own measurement, with the next id and the hue furthest from the other instruments'.
    public Instrument CreateInstrument(string name, Sample sample)
    {
        return AddNewInstrument(name, sample.Id, sample.OriginalRateC4Tuning);
    }

    // Adds an instrument on a sample, named after the sample under a name no other instrument has.
    public Instrument CreateInstrumentNamedAfter(Sample sample)
    {
        return CreateInstrument(FreeInstrumentName(sample.NameStem), sample);
    }

    // Adds an instrument on a sample under the first numbered instrument name no other instrument has.
    public Instrument CreateNumberedInstrument(Sample sample)
    {
        return CreateInstrument(FreeNumberedInstrumentName(), sample);
    }

    // Adds an instrument that plays the noise generator, with the next id and the hue furthest from the other instruments'.
    public Instrument CreateNoiseInstrument(string name)
    {
        return AddNewInstrument(name, Sample.NoiseId, Instrument.NoiseTuningMultiplier);
    }

    // Adds an instrument that plays the noise generator, named as one under a name no other instrument has.
    public Instrument CreateNamedNoiseInstrument()
    {
        return CreateNoiseInstrument(FreeInstrumentName(Instrument.NoiseNameStem));
    }

    // Adds a second instrument with the same sample and values as an existing one, under the next free copy name and the hue furthest from the other instruments'.
    public Instrument DuplicateInstrument(int id)
    {
        var sourceInstrument = _instruments.First(instrument => instrument.Id == id);
        var copiedInstrument = sourceInstrument.CopyAs(
            NextFreeId(_instruments.Select(instrument => instrument.Id)),
            FreeInstrumentName(sourceInstrument.Name),
            NextInstrumentColor());
        _instruments.Add(copiedInstrument);
        return copiedInstrument;
    }

    // How many notes, over every channel, play an instrument.
    public int NoteCountUsingInstrument(int instrumentId)
    {
        return _notes.Sum(notes => notes.Count(note => note.InstrumentId == instrumentId));
    }

    // Deletes an instrument and every note that plays it.
    public void DeleteInstrument(int id)
    {
        int previousRollTicks = RollLengthTicks;
        _instruments.RemoveAll(instrument => instrument.Id == id);
        foreach (var notes in _notes)
        {
            notes.RemoveAll(note => note.InstrumentId == id);
        }

        FollowRoll(previousRollTicks);
    }

    // Replaces both the samples and the instruments as given.
    public void SetLibraries(IReadOnlyList<Sample> samples, IReadOnlyList<Instrument> instruments)
    {
        SetSamples(samples);
        SetInstruments(instruments);
    }

    // Replaces the samples as given.
    public void SetSamples(IReadOnlyList<Sample> samples)
    {
        ReplaceContents(_samples, samples);
    }

    // Replaces the instruments as given.
    public void SetInstruments(IReadOnlyList<Instrument> instruments)
    {
        ReplaceContents(_instruments, instruments);
    }

    // Adds a new instrument with the next id and the hue furthest from the other instruments'.
    private Instrument AddNewInstrument(string name, int sampleId, double tuning)
    {
        var instrument = Instrument.Create(
            NextFreeId(_instruments.Select(existing => existing.Id)),
            name,
            sampleId,
            NextInstrumentColor(),
            tuning);
        _instruments.Add(instrument);
        return instrument;
    }

    // The color a new instrument takes: the hue furthest from those of the instruments already in the song.
    private int NextInstrumentColor()
    {
        return InstrumentPalette.ColorFurthestFrom(_instruments.Select(instrument => instrument.ColorRgb));
    }

    // Whether an instrument in the song holds a name.
    private bool HasInstrumentNamed(string name)
    {
        return InstrumentsNamed(name).Any();
    }

    // The instruments in the song holding a name.
    private IEnumerable<Instrument> InstrumentsNamed(string name)
    {
        return _instruments.Where(instrument => instrument.Name == name);
    }

    // Whether a sample file name cannot be given to a sample arriving in the song: another sample holds it, or it is the name no sample may take.
    private bool IsSampleFileNameUnavailable(string fileName)
    {
        return HasSample(fileName) || Sample.IsReservedName(Sample.NameOf(fileName));
    }

    // Whether two sample names are one name, letter case aside, as two files on disk would be.
    private static bool IsSameSampleName(string name, string otherName)
    {
        return string.Equals(name, otherName, StringComparison.OrdinalIgnoreCase);
    }

    // One past the highest id in use.
    private static int NextFreeId(IEnumerable<int> ids)
    {
        return ids.DefaultIfEmpty(0).Max() + 1;
    }
}
