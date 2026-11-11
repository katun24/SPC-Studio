using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the instruments: which ones the song uses, their labels, and the #samples and #instruments blocks.
public static partial class MmlExporter
{
    private const int FirstLabelSuffix = 2;
    private const string EntryCommentGap = "    ";
    private const string OutOfFolder = "../";

    // The distinct instruments the song plays, in the order the notes first use them, which is the order that numbers their #instruments entries.
    public static List<Instrument> UsedInstruments(Song song)
    {
        var usedInstruments = new List<Instrument>();

        for (int channel = 0; channel < Song.DriverChannelCount; channel++)
        {
            foreach (var note in PlayedNotes(song, channel))
            {
                var instrument = song.InstrumentOf(note)!;
                if (EntryIndexOf(usedInstruments, instrument) < 0)
                {
                    usedInstruments.Add(instrument);
                }
            }
        }

        return usedInstruments;
    }

    // The distinct instruments of the song's own it plays, in the order the notes first use them, which is the order that numbers their #instruments entries.
    public static List<Instrument> UsedCustomInstruments(Song song)
    {
        return CustomInstrumentsOf(UsedInstruments(song));
    }

    // The instruments of the song's own among some, in the order given.
    private static List<Instrument> CustomInstrumentsOf(List<Instrument> instruments)
    {
        return [.. instruments.Where(instrument => !instrument.IsSmw)];
    }

    // The label each instrument the song plays is written as in the channels, in the order given: an SMW instrument's own @ number, and its #instruments
    // entry's label for one of the song's own.
    private static List<string> ChannelLabels(List<Instrument> usedInstruments, List<Instrument> customInstruments, List<string> customLabels)
    {
        return [.. usedInstruments.Select(instrument => instrument.SmwNumber is int number ? AmkCommands.InstrumentSpelling(number) : customLabels[customInstruments.IndexOf(instrument)])];
    }

    // The label each #instruments entry is written as in the channels, in the block's order: an @ and the instrument's name made safe for a
    // replacement, numbered from the second where an earlier label already holds it.
    public static List<string> InstrumentLabels(IEnumerable<string> names)
    {
        var labels = new List<string>();

        foreach (string name in names)
        {
            string sanitized = string.Concat(name.Select(character => char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_'));
            string stem = sanitized.Length == 0 || char.IsAsciiDigit(sanitized[0]) ? "@_" + sanitized : "@" + sanitized;

            string label = stem;
            for (int suffix = FirstLabelSuffix; labels.Contains(label); suffix++)
            {
                label = $"{stem}_{suffix}";
            }

            labels.Add(label);
        }

        return labels;
    }

    // The position of the entry an instrument is written as: the first used instrument that sounds the same, or -1 where none does.
    private static int EntryIndexOf(List<Instrument> usedInstruments, Instrument instrument)
    {
        return usedInstruments.FindIndex(used => used.HasSameSoundAs(instrument));
    }

    // The #samples block's lines: the song's sample group and then the distinct sample files the instruments of the song's own name, or none where it
    // would hold nothing but the default group and no entry names one of its samples by its file.
    private static List<string> SamplesBlock(Song song, List<Instrument> customInstruments)
    {
        var files = SampleExport.UsedSamples(song, customInstruments).Select(sample => sample.FileName).ToList();
        bool isGroupFileNamed = customInstruments.Any(instrument => InstrumentSource(song, instrument).NamesGroupFile);

        if (files.Count == 0 && !isGroupFileNamed && song.SampleGroup is null or SampleGroups.DefaultGroup)
        {
            return [];
        }

        var entryLines = new List<string>();
        if (song.SampleGroup is { } group)
        {
            entryLines.Add(SampleGroups.DirectiveOf(group));
            if (files.Count > 0)
            {
                entryLines.Add("");
            }
        }

        entryLines.AddRange(files.Select(QuotedText));
        return DirectiveBlock(AmkDirectives.Samples, entryLines);
    }

    // The #instruments block's lines, each entry's source padded to the longest and its number in a comment behind its bytes, the tuning worked out for the
    // rate its sample plays at, or none where there is no entry to write.
    private static List<string> InstrumentsBlock(Song song, List<Instrument> usedInstruments)
    {
        if (usedInstruments.Count == 0)
        {
            return [];
        }

        var sources = usedInstruments.Select(instrument => InstrumentSource(song, instrument).Text).ToList();
        int sourceWidth = LongestLength(sources);

        return DirectiveBlock(
            AmkDirectives.Instruments,
            usedInstruments.Select((instrument, entryIndex) =>
                $"{sources[entryIndex].PadRight(sourceWidth)} {instrument.ToMmlBytes(song.RateFractionOf(instrument))}{EntryCommentGap}; {InstrumentNumberWritten(entryIndex)}"));
    }

    // What an #instruments entry names before its bytes: the noise generator, the SMW instrument whose sample of the sample group's it plays, or its
    // quoted sample - one of the sample group's by its path out of the #path folder - and whether it names a sample of the group's by its file.
    private static (string Text, bool NamesGroupFile) InstrumentSource(Song song, Instrument instrument)
    {
        if (instrument.IsNoise)
        {
            return (WrittenTokens.HexLettered('n', SnesNoise.MinClock), false);
        }

        if (SmwInstruments.FirstNumberPlaying(instrument.SampleId) is int number)
        {
            return (AmkCommands.InstrumentSpelling(number), false);
        }

        var sample = song.SampleFor(instrument);
        if (sample?.PathInSampleGroup is { } pathInGroup)
        {
            return (QuotedText(PathOutOfSampleFolder(song) + pathInGroup), true);
        }

        return (QuotedText(sample?.FileName ?? ""), false);
    }

    // What steps a sample's name out of the #path folder to the samples folder it is in: nothing where the folder is the samples folder itself.
    private static string PathOutOfSampleFolder(Song song)
    {
        return song.HasSamplePath ? OutOfFolder : "";
    }

    // The replacement directives that define each entry's label as its @ number, each find padded to the longest.
    private static IEnumerable<string> LabelDirectives(List<string> labels)
    {
        return ReplacementDirectives(labels.Select((label, entryIndex) => (label, InstrumentNumberWritten(entryIndex))));
    }

    // The @ number an #instruments entry is played under.
    private static string InstrumentNumberWritten(int entryIndex)
    {
        return AmkCommands.InstrumentSpelling(FirstCustomInstrument + entryIndex);
    }
}
