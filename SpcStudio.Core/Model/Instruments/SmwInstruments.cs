namespace SpcStudio.Model;

// SMW's own instruments as instruments of a song: AddmusicK's built-ins, each playing the sample the song's sample group holds at its position.
public static class SmwInstruments
{
    public const int PianoNumber = 9;
    private const int FirstSampleId = -100;
    private const string NamePrefix = "SMW @";
    private const string SoundNameSeparator = " - ";

    // What each SMW instrument sounds like, by its number, as its name says it.
    private static readonly Dictionary<int, string> SoundNamesByNumber = new()
    {
        [0] = "Flute",
        [1] = "Violin",
        [2] = "Glockenspiel",
        [3] = "Marimba",
        [4] = "Cello",
        [5] = "Steel Guitar",
        [6] = "Trumpet",
        [7] = "Steel Drum",
        [8] = "Acoustic Bass",
        [9] = "Piano",
        [10] = "Snare Drum",
        [11] = "Violin 2",
        [12] = "Bongo",
        [13] = "Electric Piano",
        [14] = "Slap Bass",
        [15] = "Orchestra Hit",
        [16] = "Harp",
        [17] = "Distortion Guitar",
        [18] = "Violin 3",
        [21] = "Bass Drum",
        [22] = "Cymbal",
        [23] = "Maracas",
        [24] = "Wood Block (Low)",
        [25] = "Wood Block (High)",
        [26] = "Low Drum",
        [27] = "Snare Drum",
        [28] = "Snare Drum (Soft)",
        [29] = "Power Drum",
    };

    // Every number an SMW instrument is played under, the pitched ones first and then the percussion rows.
    public static IEnumerable<int> Numbers
    {
        get
        {
            return Enumerable.Range(0, MmlBuiltins.MaxPitchedNumber + 1)
                .Concat(Enumerable.Range(MmlBuiltins.FirstPercussionNumber, MmlBuiltins.LastPercussionNumber - MmlBuiltins.FirstPercussionNumber + 1));
        }
    }

    // The id the sample at a position of the sample group is known by, which no sample of the song's own can hold.
    public static int SampleIdOf(int position)
    {
        return FirstSampleId - position;
    }

    // Whether a sample id is one a position of the sample group gives, rather than one of a song's own samples or the noise generator.
    public static bool IsSampleId(int sampleId)
    {
        return sampleId <= FirstSampleId;
    }

    // The lowest number an SMW instrument plays a sample id under, or null where no SMW instrument plays it.
    public static int? FirstNumberPlaying(int sampleId)
    {
        return Numbers.Where(number => SampleIdOf(MmlBuiltins.SampleNumberOf(number)) == sampleId).Cast<int?>().FirstOrDefault();
    }

    // The name an SMW instrument is shown under: its number, then what it sounds like.
    public static string NameOf(int number)
    {
        return NamePrefix + number + SoundNameSeparator + SoundNamesByNumber[number];
    }

    // An SMW instrument under an id and a color, holding the driver's own bytes for its number and playing the sample at its position.
    public static Instrument Create(int id, int number, int colorRgb)
    {
        return new Instrument
        {
            Id = id,
            Name = NameOf(number),
            SampleId = SampleIdOf(MmlBuiltins.SampleNumberOf(number)),
            ColorRgb = colorRgb,
            SmwNumber = number,
            Bytes = MmlBuiltins.EntryFor(number).Bytes,
        };
    }

    // The instrument an SMW instrument started out as, holding SMW's own values for its number under its id and color.
    public static Instrument OriginalOf(Instrument instrument)
    {
        return Create(instrument.Id, instrument.SmwNumber!.Value, instrument.ColorRgb);
    }
}
