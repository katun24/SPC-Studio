using System.Text;
using SpcStudio.Import;
using SpcStudio.Model;
using WrittenChannel = (int Channel, string Header, string Body);

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the file as a whole: its blocks in order, the optimizations run over the channels, the
// song-wide header, each channel's header and how a line of channel data is wrapped.
public static partial class MmlExporter
{
    public const int AmkVersion = MmlPreprocessor.NewestDialect;
    public const int FirstCustomInstrument = 30;
    public const int CommandChannel = 0;
    public const int MaxLineLength = 200;
    private const int BlockBreakLineCount = 3;
    private const int FirstFreeSlotPastLabelCount = 2;
    private const string BlockOpening = "{";
    private const string BlockClosing = "}";
    private const string EntryIndent = "\t";
    private const string QuoteMark = "\"";

    internal static readonly string DialectLine = $"{AmkDirectives.Written(AmkDirectives.Amk)} {AmkVersion}";
    private static readonly int WrittenSpcKeyWidth = AmkDirectives.Written(AmkDirectives.CommentField).Length;
    private static readonly string EmptyLinesBetweenBlocks = string.Concat(Enumerable.Repeat(Environment.NewLine, BlockBreakLineCount));

    // Writes the song as AddmusicK MML under the optimizations the options name, leaving out the channels with nothing to write.
    public static string Build(Song song, MmlExportOptions options)
    {
        var usedInstruments = UsedInstruments(song);
        var labels = InstrumentLabels(usedInstruments.Select(instrument => instrument.Name));
        var globalHeader = GlobalHeader(song).ToList();
        var (channels, remoteBlocks, phrases) = Optimized(song, WrittenChannels(song, usedInstruments, labels, options.HeldNoteShapeWritten()), options);
        var remoteBlockContents = remoteBlocks.Select(block => block[(block.IndexOf('[') + 1)..^1]);

        var blocks = new List<string>();
        AddBlock(blocks, [DialectLine]);
        AddBlock(blocks, SpcBlock(song.SpcTags));
        AddBlock(blocks, OptionLines(song));
        AddBlock(blocks, [$"{AmkDirectives.Written(AmkDirectives.Path)} {QuotedText(song.SamplePath)}"]);
        AddBlock(blocks, SamplesBlock(song, usedInstruments));
        AddBlock(blocks, InstrumentsBlock(song, usedInstruments));
        AddBlock(blocks, LabelDirectives(labels));
        AddBlock(blocks, CommandNameDirectives(
            globalHeader.Concat(remoteBlockContents)
                .Concat(phrases.Select(phrase => phrase.Text))
                .Concat(channels.SelectMany(channel => new[] { channel.Header, channel.Body }))));
        AddBlock(blocks, remoteBlocks);
        AddBlock(blocks, PhraseDirectives(phrases.Where(phrase => phrase.IsShared)));
        AddBlock(blocks, globalHeader);

        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            AddBlock(blocks, ChannelBlock(channels[channelIndex], channelIndex, phrases));
        }

        return string.Join(EmptyLinesBetweenBlocks, blocks) + Environment.NewLine;
    }

    // Every channel the export writes, with its header and its body as the notes and the lanes have them.
    private static List<WrittenChannel> WrittenChannels(Song song, List<Instrument> usedInstruments, IReadOnlyList<string> labels, HeldNoteShape shape)
    {
        return Enumerable.Range(0, Song.DriverChannelCount)
            .Where(channel => WritesChannel(song, channel))
            .Select(channel => (channel, ChannelHeader(song, channel, shape), ChannelBodyText(song, channel, usedInstruments, labels, shape)))
            .ToList();
    }

    // The channels with the optimizations the options name run over them, the remote code blocks the channels call and the phrases their loops play.
    private static (List<WrittenChannel> Channels, List<string> RemoteBlocks, List<LoopCompression.Phrase> Phrases) Optimized(Song song, List<WrittenChannel> channels, MmlExportOptions options)
    {
        int loopLabelCount = 0;
        if (options.Loops)
        {
            (var loopedBodies, loopLabelCount) = LoopCompression.Apply(channels.Select(channel => (channel.Header, channel.Body)).ToList());
            channels = WithBodies(channels, loopedBodies);
        }

        var remoteBlocks = new List<string>();
        if (HoldToggles.IsKeyOffBlockUsed(song, options.HeldNoteShapeWritten()))
        {
            remoteBlocks.Add(HoldBlock);
        }

        int nextNumber = FirstFreeRemoteNumber(remoteBlocks.Count, loopLabelCount);
        int? legatoHoldNumber = null;
        if (channels.Any(channel => WrittenTokens.Split(channel.Body).Contains(LegatoHoldArmPlaceholder)))
        {
            legatoHoldNumber = nextNumber;
            remoteBlocks.Add(RemoteBlockDefinition(nextNumber, string.Join(' ', LegatoHoldBlockCommands())));
            nextNumber++;
        }

        if (options.KeyOnRemoteCode)
        {
            var (armedBodies, definitions) = KeyOnBlocks.Apply(channels.Select(channel => (channel.Header, channel.Body)).ToList(), song.IsLooping, nextNumber);

            channels = WithBodies(channels, armedBodies);
            remoteBlocks.AddRange(definitions);
        }

        if (legatoHoldNumber is int number)
        {
            channels = WithBodies(channels, channels.Select(channel => WithLegatoHoldArmsNumbered(channel.Body, number)).ToList());
        }

        var phrases = new List<LoopCompression.Phrase>();
        if (options.Loops)
        {
            (var namedBodies, phrases) = LoopCompression.WithPhrasesNamed(channels.Select(channel => channel.Body).ToList());
            channels = WithBodies(channels, namedBodies);
        }

        return (channels, remoteBlocks, phrases);
    }

    // The first remote code number past the blocks already numbered and the slots the loops' labels take.
    private static int FirstFreeRemoteNumber(int numberedBlockCount, int loopLabelCount)
    {
        int nextNumber = numberedBlockCount + 1;
        return loopLabelCount > 0 ? Math.Max(nextNumber, loopLabelCount + FirstFreeSlotPastLabelCount) : nextNumber;
    }

    // A channel body with every stand-in for the legato's key-on block arm replaced by the arm of the block's own number.
    private static string WithLegatoHoldArmsNumbered(string body, int blockNumber)
    {
        return string.Join(' ', WrittenTokens.Split(body).Select(token => token == LegatoHoldArmPlaceholder ? KeyOnArmCommandFor(blockNumber) : token));
    }

    // The channels with their bodies replaced, in the same order.
    private static List<WrittenChannel> WithBodies(List<WrittenChannel> channels, List<string> bodies)
    {
        return channels.Select((channel, channelIndex) => channel with { Body = bodies[channelIndex] }).ToList();
    }

    // Adds the lines as one block of the file, adding nothing where they hold nothing.
    private static void AddBlock(List<string> blocks, IEnumerable<string> lines)
    {
        string block = string.Join(Environment.NewLine, lines).TrimEnd();
        if (block.Length > 0)
        {
            blocks.Add(block);
        }
    }

    // The #spc block's lines, one for each of the four fields holding text, or none where none does.
    private static List<string> SpcBlock(SpcTags tags)
    {
        if (tags.IsEmpty)
        {
            return [];
        }

        var fields = new[] { (Key: AmkDirectives.TitleField, Value: tags.Title), (Key: AmkDirectives.AuthorField, Value: tags.Author), (Key: AmkDirectives.GameField, Value: tags.Game), (Key: AmkDirectives.CommentField, Value: tags.Comment) };
        return DirectiveBlock(
            AmkDirectives.Spc,
            fields.Where(field => !string.IsNullOrWhiteSpace(field.Value))
                .Select(field => $"{AmkDirectives.Written(field.Key).PadRight(WrittenSpcKeyWidth)} {QuotedText(field.Value.Replace(QuoteMark, "'"))}"));
    }

    // A directive's block: its name, the opening brace, each entry indented on a line of its own, an empty line left empty, and the closing brace.
    private static List<string> DirectiveBlock(string directiveName, IEnumerable<string> entryLines)
    {
        return [AmkDirectives.Written(directiveName), BlockOpening, .. entryLines.Select(line => line.Length == 0 ? line : EntryIndent + line), BlockClosing];
    }

    // A text in quote marks, as the file writes a string.
    private static string QuotedText(string text)
    {
        return QuoteMark + text + QuoteMark;
    }

    // The #option lines the song asks for.
    private static IEnumerable<string> OptionLines(Song song)
    {
        if (song.UsesSmwVelocities)
        {
            yield return OptionLine(AmkDirectives.SmwVelocityTableOption);
        }

        if (song.HasTempoImmunity)
        {
            yield return OptionLine(AmkDirectives.TempoImmunityOption);
        }

        if (!song.IsLooping)
        {
            yield return OptionLine(AmkDirectives.NoLoopOption);
        }
    }

    // An #option line naming one option.
    private static string OptionLine(string optionName)
    {
        return $"{AmkDirectives.Written(AmkDirectives.Option)} {optionName}";
    }

    // The state the song as a whole starts in, one command a line: the tempo, the song volume, the echo and the light staccato.
    private static IEnumerable<string> GlobalHeader(Song song)
    {
        yield return LetteredText('t', song.BaseTempo);
        yield return LetteredText('w', song.BaseMasterVolume);

        foreach (string command in EchoHeader(song))
        {
            yield return command;
        }

        if (StaccatoHeader(song) is { } staccato)
        {
            yield return staccato;
        }
    }

    // What a channel's #N line carries ahead of its notes: the state the channel starts in.
    private static string ChannelHeader(Song song, int channel, HeldNoteShape shape)
    {
        return $"{LetteredText('v', LaneRegions.ChannelVolumeWritten(song, channel).Base)} " +
            $"{PanHeader(song, channel)} " +
            AmplifyHeader(song, channel) +
            SweepHeader(VibratoMap.For(song, channel), VibratoCommandFor) +
            SweepHeader(TremoloMap.For(song, channel), TremoloCommandFor) +
            LegatoHeader(song, channel, shape) +
            HoldHeader(song, channel, shape);
    }

    // One channel's block: its banner, the phrases only it plays, its header and its body.
    private static List<string> ChannelBlock(WrittenChannel channel, int channelIndex, List<LoopCompression.Phrase> phrases)
    {
        var lines = new List<string> { $";---------------- CHANNEL {channel.Channel + 1} ----------------;" };

        var ownPhraseLines = PhraseDirectives(phrases.Where(phrase => !phrase.IsShared && phrase.ChannelIndexes.Contains(channelIndex))).ToList();
        if (ownPhraseLines.Count > 0)
        {
            lines.AddRange(ownPhraseLines);
            lines.Add("");
        }

        lines.AddRange(WrappedLines($"#{channel.Channel} {channel.Header}"));
        lines.AddRange(WrappedLines(LoopCompression.WithoutSpacesInsideBrackets(channel.Body)));
        return lines;
    }

    // One line of channel data as the lines it is written over, broken in front of the token that would take a line past its longest and never inside
    // one, the lead opening the first line and the indent every line after it.
    private static IEnumerable<string> WrappedLines(string line, string lead = "", string indent = "")
    {
        var current = new StringBuilder(lead);
        int start = lead.Length;

        foreach (string token in WrittenTokens.Split(line))
        {
            if (current.Length > start && current.Length + 1 + token.Length > MaxLineLength)
            {
                yield return current.ToString();
                current.Clear().Append(indent);
                start = indent.Length;
            }

            if (current.Length > start)
            {
                current.Append(' ');
            }

            current.Append(token);
        }

        if (current.Length > start)
        {
            yield return current.ToString();
        }
    }

    // The replacement directives that define the phrases, each find padded to the longest and each body carried over lines as channel data is.
    private static IEnumerable<string> PhraseDirectives(IEnumerable<LoopCompression.Phrase> phrases)
    {
        var phraseList = phrases.ToList();
        int width = LongestLength(phraseList.Select(phrase => phrase.Name));

        return phraseList.SelectMany(phrase =>
        {
            string lead = ReplacementLead(phrase.Name, width);
            return WrappedLines(phrase.Text + QuoteMark, lead, new string(' ', lead.Length));
        });
    }

    // One replacement directive a line for each pair, each find padded to the longest.
    private static IEnumerable<string> ReplacementDirectives(IEnumerable<(string Find, string Replacement)> pairs)
    {
        var pairList = pairs.ToList();
        int width = LongestLength(pairList.Select(pair => pair.Find));

        return pairList.Select(pair => ReplacementLead(pair.Find, width) + pair.Replacement + QuoteMark);
    }

    // A replacement directive up to its replacement: the opening quote mark, the find padded to a width, and the equals sign.
    private static string ReplacementLead(string find, int findWidth)
    {
        return $"{QuoteMark}{find.PadRight(findWidth)} = ";
    }

    // The length of the longest of some texts, or none where there are none.
    private static int LongestLength(IEnumerable<string> texts)
    {
        return texts.Select(text => text.Length).DefaultIfEmpty().Max();
    }
}
