using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the instruments: which ones the song uses, their labels, and the #samples and #instruments blocks.
public static partial class MmlExporter
{
    private const int FirstLabelSuffix = 2;
    private const string EntryCommentGap = "    ";

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

    // The #samples block's lines: the song's sample group and then the distinct sample files the used instruments name, or none where there is neither.
    private static List<string> SamplesBlock(Song song, List<Instrument> usedInstruments)
    {
        var files = SampleExport.UsedSamples(song, usedInstruments).Select(sample => sample.FileName).ToList();

        if (files.Count == 0 && song.SampleGroup is null)
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
    // rate its sample plays at.
    private static List<string> InstrumentsBlock(Song song, List<Instrument> usedInstruments)
    {
        var sources = usedInstruments.Select(instrument => InstrumentSource(song, instrument)).ToList();
        int sourceWidth = LongestLength(sources);

        return DirectiveBlock(
            AmkDirectives.Instruments,
            usedInstruments.Select((instrument, entryIndex) =>
                $"{sources[entryIndex].PadRight(sourceWidth)} {instrument.ToMmlBytes(song.RateFractionOf(instrument))}{EntryCommentGap}; {InstrumentNumberWritten(entryIndex)}"));
    }

    // What an #instruments entry names before its bytes: its quoted sample, or the noise generator.
    private static string InstrumentSource(Song song, Instrument instrument)
    {
        return instrument.IsNoise ? WrittenTokens.HexLettered('n', SnesNoise.MinClock) : QuotedText(song.SampleFor(instrument)?.FileName ?? "");
    }

    // The replacement directives that define each entry's label as its @ number, each find padded to the longest.
    private static IEnumerable<string> LabelDirectives(List<string> labels)
    {
        return ReplacementDirectives(labels.Select((label, entryIndex) => (label, InstrumentNumberWritten(entryIndex))));
    }

    // The @ number an #instruments entry is played under.
    private static string InstrumentNumberWritten(int entryIndex)
    {
        return $"@{FirstCustomInstrument + entryIndex}";
    }
}
