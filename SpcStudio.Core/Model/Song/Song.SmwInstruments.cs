using SpcStudio.Audio;

namespace SpcStudio.Model;

// The song's SMW instruments: turning them on and off, and the samples they play out of the song's sample group.
public sealed partial class Song
{
    private readonly Dictionary<int, Sample> _smwSamplesById = [];

    public bool UsesSmwInstruments { get; set; }

    // The samples the SMW instruments play, each under the id its position in the sample group gives it.
    public IReadOnlyCollection<Sample> SmwSamples
    {
        get
        {
            return _smwSamplesById.Values;
        }
    }

    // The SMW instruments the song's notes play, each once.
    public IReadOnlyList<Instrument> SmwInstrumentsPlayed
    {
        get
        {
            return [.. InstrumentsPlayed.Where(instrument => instrument.IsSmw).Distinct()];
        }
    }

    // Whether any note of the song plays an SMW instrument.
    public bool HasSmwNotes
    {
        get
        {
            return SmwInstrumentsPlayed.Count > 0;
        }
    }

    // Whether any note of the song plays a sample of the sample group's, through an SMW instrument or one of the song's own.
    public bool HasSmwSampleNotes
    {
        get
        {
            return InstrumentsPlayed.Any(instrument => SmwInstruments.IsSampleId(instrument.SampleId));
        }
    }

    // The ids of the sample group's samples the song cannot do without: those of the SMW instruments its notes play and those its own instruments play.
    public IReadOnlySet<int> SmwSampleIdsNeeded
    {
        get
        {
            return SmwInstrumentsPlayed.Concat(OwnInstrumentsOnSmwSamples).Select(instrument => instrument.SampleId).ToHashSet();
        }
    }

    // Whether the SMW instruments can be turned off: no note plays one and no instrument of the song's own plays a sample of the sample group's.
    public bool CanTurnOffSmwInstruments
    {
        get
        {
            return !HasSmwNotes && !OwnInstrumentsOnSmwSamples.Any();
        }
    }

    // The sample group's samples a Sample drop-down offers, in position order, while the SMW instruments are on.
    public IReadOnlyList<Sample> PickableSmwSamples
    {
        get
        {
            if (!UsesSmwInstruments)
            {
                return [];
            }

            return [.. _smwSamplesById.Values.OrderByDescending(sample => sample.Id)];
        }
    }

    // The instruments a drop-down offers: every one of the song's own, and every SMW instrument whose sample the sample group holds.
    public IReadOnlyList<Instrument> PickableInstruments
    {
        get
        {
            return [.. Instruments.Where(instrument => !instrument.IsSmw || SampleFor(instrument) is not null)];
        }
    }

    // The instrument a new note is given where nothing else chose one: SMW's piano where it can be picked, and otherwise the first instrument that can.
    public Instrument? StartingInstrument
    {
        get
        {
            var pickable = PickableInstruments;
            return pickable.FirstOrDefault(instrument => instrument.SmwNumber == SmwInstruments.PianoNumber) ?? pickable.FirstOrDefault();
        }
    }

    // How many of the SMW instruments hold any value other than SMW's own, their colors aside.
    public int ChangedSmwInstrumentCount
    {
        get
        {
            return _instruments.Count(instrument => instrument.HasChangesFromSmw);
        }
    }

    // The instrument each note of the song plays, once per note, leaving out a note whose instrument the song does not hold.
    private IEnumerable<Instrument> InstrumentsPlayed
    {
        get
        {
            return _notes.SelectMany(notes => notes).Select(InstrumentOf).OfType<Instrument>();
        }
    }

    // The song's own instruments that play a sample of the sample group's.
    private IEnumerable<Instrument> OwnInstrumentsOnSmwSamples
    {
        get
        {
            return _instruments.Where(instrument => instrument.IsOnSmwSample);
        }
    }

    // Turns the SMW instruments on, adding each one the song does not hold yet.
    public void TurnOnSmwInstruments()
    {
        UsesSmwInstruments = true;
        foreach (int number in SmwInstruments.Numbers.Where(number => SmwInstrument(number) is null))
        {
            _instruments.Add(SmwInstruments.Create(NextFreeId(_instruments.Select(instrument => instrument.Id)), number, NextInstrumentColor()));
        }
    }

    // Turns the SMW instruments off, taking every one of them out of the song.
    public void TurnOffSmwInstruments()
    {
        UsesSmwInstruments = false;
        _instruments.RemoveAll(instrument => instrument.IsSmw);
    }

    // The SMW instrument of a number, or null where the song does not hold it.
    public Instrument? SmwInstrument(int number)
    {
        return _instruments.FirstOrDefault(instrument => instrument.SmwNumber == number);
    }

    // Puts an SMW instrument back at SMW's own values for its number, keeping its color.
    public void RevertSmwInstrument(int id)
    {
        int instrumentIndex = _instruments.FindIndex(instrument => instrument.Id == id);
        _instruments[instrumentIndex] = SmwInstruments.OriginalOf(_instruments[instrumentIndex]);
    }

    // Adds an instrument on the sample SMW's piano plays under the first numbered instrument name no other instrument has, or returns null where the song
    // holds no SMW piano or no sample for it.
    public Instrument? CreateInstrumentOnSmwPianoSample()
    {
        if (SmwInstrument(SmwInstruments.PianoNumber) is not { } piano || SampleFor(piano) is not { } pianoSample)
        {
            return null;
        }

        return CreateNumberedInstrument(pianoSample);
    }

    // Replaces the samples the SMW instruments play as given.
    public void SetSmwSamples(IEnumerable<Sample> samples)
    {
        _smwSamplesById.Clear();
        foreach (var sample in samples)
        {
            _smwSamplesById[sample.Id] = sample;
        }
    }
}
