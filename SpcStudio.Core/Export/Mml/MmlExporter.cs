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
    private const int BlockBreakLineCount = 3;
    private const int FirstFreeSlotPastLabelCount = 2;
    private const string BlockOpening = "{";
    private const string BlockClosing = "}";
    private const string EntryIndent = "\t";
    private const string QuoteMark = "\"";
    private const string BuiltinTransposeOff = "h0";
    private const int MaxBarredReleaseNotesPerChannel = 8;

    internal static readonly string DialectLine = AmkDirectives.WrittenAmk(AmkVersion);
    private static readonly int WrittenSpcKeyWidth = AmkDirectives.Written(AmkDirectives.CommentField).Length;
    private static readonly string EmptyLinesBetweenBlocks = string.Concat(Enumerable.Repeat(Environment.NewLine, BlockBreakLineCount));

    // Writes the song as AddmusicK MML under the optimizations the options name, its loop shortened where they say so, leaving out the channels with
    // nothing to write.
    public static string Build(Song song, MmlExportOptions options)
    {
        var writtenSong = SongLoopShortening.Shortened(song, options);
        var arpeggioRunsByChannel = options.ArpeggiosAsFb ? ArpeggioRuns.ForEachChannel(writtenSong, options) : ArpeggioRuns.NoneOnAnyChannel();
        return Build(writtenSong, options, arpeggioRunsByChannel);
    }

    // Writes the song as AddmusicK MML under the optimizations the options name, each channel's arpeggio runs written under an $FB, with the release
    // runs where the options switch the key-on blocks on.
    private static string Build(Song song, MmlExportOptions options, IReadOnlyList<ArpeggioRun>[] arpeggioRunsByChannel)
    {
        var releaseRunsByChannel = options.KeyOnRemoteCode ? ReleaseRuns.ForEachChannel(song, options, arpeggioRunsByChannel) : ReleaseRuns.NoneOnAnyChannel();
        return WrittenWithRuns(song, options, arpeggioRunsByChannel, releaseRunsByChannel);
    }

    // Writes the song as AddmusicK MML under the optimizations the options name, each channel's arpeggio runs written under an $FB and its release
    // runs under their key-off blocks, looping inside its release runs where the import reads the channel back as it does with no loop there: a note
    // read back otherwise is barred from having a loop stand behind it, and a channel with more such notes than a few loops outside its runs alone.
    private static string WrittenWithRuns(Song song, MmlExportOptions options, IReadOnlyList<ArpeggioRun>[] arpeggioRunsByChannel, IReadOnlyList<ReleaseRun>[] releaseRunsByChannel)
    {
        // Every channel with a release run, each with no note barred yet.
        var barredReleaseNotesByChannel = Enumerable.Range(0, Song.DriverChannelCount)
            .Where(channel => releaseRunsByChannel[channel].Count > 0)
            .ToDictionary(channel => channel, _ => new HashSet<int>());
        string text = WrittenWithRunsLooping(song, options, arpeggioRunsByChannel, releaseRunsByChannel, barredReleaseNotesByChannel);
        if (!options.Loops || barredReleaseNotesByChannel.Count == 0)
        {
            return text;
        }

        string textLoopingOutsideReleaseRuns = WrittenWithRunsLooping(song, options, arpeggioRunsByChannel, releaseRunsByChannel, new Dictionary<int, HashSet<int>>());
        if (ReadBacks.ReadBack(textLoopingOutsideReleaseRuns) is not { } readBackLoopingOutside)
        {
            return textLoopingOutsideReleaseRuns;
        }

        while (barredReleaseNotesByChannel.Count > 0)
        {
            if (ReadBacks.ReadBack(text) is not { } readBack || ReadBacks.NewWarnings(readBackLoopingOutside, readBack).Count > 0)
            {
                return textLoopingOutsideReleaseRuns;
            }

            var ticksApartByChannel = ReadBacks.FirstTicksApartByChannel(readBackLoopingOutside, readBack);
            if (ticksApartByChannel.Count == 0)
            {
                return text;
            }

            foreach (var (channel, tickApart) in ticksApartByChannel)
            {
                if (!barredReleaseNotesByChannel.TryGetValue(channel, out var barredNoteTicks))
                {
                    return textLoopingOutsideReleaseRuns;
                }

                if (tickApart is not int noteTick || barredNoteTicks.Count >= MaxBarredReleaseNotesPerChannel || !barredNoteTicks.Add(noteTick))
                {
                    barredReleaseNotesByChannel.Remove(channel);
                }
            }

            text = WrittenWithRunsLooping(song, options, arpeggioRunsByChannel, releaseRunsByChannel, barredReleaseNotesByChannel);
        }

        return text;
    }

    // Writes the song as AddmusicK MML under the optimizations the options name, each channel's arpeggio runs written under an $FB and its release
    // runs under their key-off blocks, the channels given looping inside their release runs too but behind the notes barred there.
    private static string WrittenWithRunsLooping(
        Song song, MmlExportOptions options, IReadOnlyList<ArpeggioRun>[] arpeggioRunsByChannel, IReadOnlyList<ReleaseRun>[] releaseRunsByChannel,
        IReadOnlyDictionary<int, HashSet<int>> barredReleaseNotesByChannel)
    {
        var usedInstruments = UsedInstruments(song);
        var customInstruments = CustomInstrumentsOf(usedInstruments);
        var customLabels = InstrumentLabels(customInstruments.Select(instrument => instrument.Name));
        var labels = ChannelLabels(usedInstruments, customInstruments, customLabels);
        var globalHeader = GlobalHeader(song).ToList();
        var shape = options.HeldNoteShapeWritten();
        var releaseBlocks = ReleaseRuns.BlocksOf(releaseRunsByChannel);
        var writtenChannels = WrittenChannels(song, usedInstruments, labels, options, arpeggioRunsByChannel, releaseRunsByChannel, releaseBlocks);
        var (channels, remoteBlocks, phrases) = Optimized(song, writtenChannels, options, releaseBlocks, barredReleaseNotesByChannel);
        var remoteBlockContents = remoteBlocks.Select(block => block[(block.IndexOf('[') + 1)..^1]);

        var blocks = new List<string>();
        AddBlock(blocks, [DialectLine]);
        AddBlock(blocks, SpcBlock(song.SpcTags));
        AddBlock(blocks, OptionLines(song));
        AddBlock(blocks, PathBlock(song));
        AddBlock(blocks, SamplesBlock(song, customInstruments));
        AddBlock(blocks, InstrumentsBlock(song, customInstruments));
        AddBlock(blocks, LabelDirectives(customLabels));
        AddBlock(blocks, CommandNameDirectives(
            globalHeader.Concat(remoteBlockContents)
                .Concat(phrases.Select(phrase => phrase.Text))
                .Concat(channels.SelectMany(channel => new[] { channel.Header, channel.Body }))));
        AddBlock(blocks, remoteBlocks);
        AddBlock(blocks, PhraseDirectives(phrases.Where(phrase => phrase.IsShared), options.LineLengthLimit));
        AddBlock(blocks, globalHeader);

        var writtenTicks = new WrittenTicks(phrases);
        int? ticksPerBarGroup = options.BarsPerLine * song.TimeSignature.TicksPerBar;
        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            AddBlock(blocks, ChannelBlock(channels[channelIndex], channelIndex, phrases, options.LineLengthLimit, ticksPerBarGroup, writtenTicks));
        }

        return string.Join(EmptyLinesBetweenBlocks, blocks) + Environment.NewLine;
    }

    // Every channel the export writes, with its header and its body as the notes and the lanes have them, its arpeggio runs under an $FB, its release
    // runs under their key-off blocks and its runs of notes sharing a first bend under a pitch envelope where the options name it.
    private static List<WrittenChannel> WrittenChannels(
        Song song, List<Instrument> usedInstruments, IReadOnlyList<string> labels, MmlExportOptions options, IReadOnlyList<ArpeggioRun>[] arpeggioRunsByChannel,
        IReadOnlyList<ReleaseRun>[] releaseRunsByChannel, IReadOnlyList<string> releaseBlocks)
    {
        return Enumerable.Range(0, Song.DriverChannelCount)
            .Where(channel => WritesChannel(song, channel))
            .Select(channel => (
                channel,
                ChannelHeader(song, channel, options.HeldNoteShapeWritten()),
                ChannelBodyText(song, channel, usedInstruments, labels, options, arpeggioRunsByChannel[channel], releaseRunsByChannel[channel], releaseBlocks)))
            .ToList();
    }

    // The channels with the optimizations the options name run over them, the remote code blocks the channels call and the phrases their loops play, the
    // release runs' blocks given in the order their arms' placeholders number them and the channels given looping inside their release runs too but
    // behind the notes barred there.
    private static (List<WrittenChannel> Channels, List<string> RemoteBlocks, List<LoopCompression.Phrase> Phrases) Optimized(
        Song song, List<WrittenChannel> channels, MmlExportOptions options, IReadOnlyList<string> releaseBlocks, IReadOnlyDictionary<int, HashSet<int>> barredReleaseNotesByChannel)
    {
        int loopLabelCount = 0;
        if (options.Loops)
        {
            var headersAndBodies = HeadersAndBodies(channels);

            // The notes barred on each written channel that loops inside its release runs, by the channel's place among the written ones.
            var barredByIndex = Enumerable.Range(0, channels.Count)
                .Where(index => barredReleaseNotesByChannel.ContainsKey(channels[index].Channel))
                .ToDictionary(index => index, index => (IReadOnlySet<int>)barredReleaseNotesByChannel[channels[index].Channel]);
            (var loopedBodies, loopLabelCount) = LoopCompression.Apply(headersAndBodies, barredByIndex, options);
            channels = WithBodies(channels, loopedBodies);
        }

        var remoteBlocks = new List<string>();
        if (HoldToggles.IsKeyOffBlockUsed(song, options.HeldNoteShapeWritten()))
        {
            remoteBlocks.Add(RemoteBlockDefinition(HoldBlockNumber, ""));
        }

        int nextNumber = FirstFreeRemoteNumber(remoteBlocks.Count, loopLabelCount);
        var armsByPlaceholder = new Dictionary<string, string>();

        // Every token the channels write, among which the stand-ins for what the key-on slot holds between runs are looked for.
        var writtenTokens = channels.SelectMany(channel => WrittenTokens.Split(channel.Body)).ToHashSet();
        foreach (string placeholder in KeyOnSlotArmPlaceholders.Where(writtenTokens.Contains))
        {
            armsByPlaceholder[placeholder] = KeyOnArmCommandFor(nextNumber);
            remoteBlocks.Add(RemoteBlockDefinition(nextNumber, string.Join(' ', KeyOnSlotBlockCommandsOf(placeholder)!)));
            nextNumber++;
        }

        for (int blockIndex = 0; blockIndex < releaseBlocks.Count; blockIndex++)
        {
            armsByPlaceholder[ReleaseRuns.ArmPlaceholder(blockIndex)] = KeyOffArmCommandFor(nextNumber);
            remoteBlocks.Add(RemoteBlockDefinition(nextNumber, releaseBlocks[blockIndex]));
            nextNumber++;
        }

        if (options.KeyOnRemoteCode)
        {
            var (armedBodies, definitions) = KeyOnBlocks.Apply(HeadersAndBodies(channels), song.IsLooping, nextNumber);

            channels = WithBodies(channels, armedBodies);
            remoteBlocks.AddRange(definitions);
            nextNumber += definitions.Count;
        }

        if (options.RepeatedMidNoteCommandsAsRemoteCode)
        {
            var (timedBodies, definitions) = TimerBlocks.Apply(HeadersAndBodies(channels), song.IsLooping, nextNumber);

            channels = WithBodies(channels, timedBodies);
            remoteBlocks.AddRange(definitions);
        }

        if (armsByPlaceholder.Count > 0)
        {
            channels = WithBodies(channels, channels.Select(channel => WithArmsNumbered(channel.Body, armsByPlaceholder)).ToList());
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

    // A channel body with every stand-in for a block's arm replaced by the arm of the block's own number.
    private static string WithArmsNumbered(string body, Dictionary<string, string> armsByPlaceholder)
    {
        return string.Join(' ', WrittenTokens.Split(body).Select(token => armsByPlaceholder.GetValueOrDefault(token, token)));
    }

    // Each channel's header and body, in the same order.
    private static List<(string Header, string Body)> HeadersAndBodies(List<WrittenChannel> channels)
    {
        return [.. channels.Select(channel => (channel.Header, channel.Body))];
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

    // The #path line naming the song's folder for its samples, or none where it names none.
    private static List<string> PathBlock(Song song)
    {
        if (!song.HasSamplePath)
        {
            return [];
        }

        return [$"{AmkDirectives.Written(AmkDirectives.Path)} {QuotedText(song.SamplePath)}"];
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
        return BuiltinTransposeHeader(song, channel) +
            $"{LetteredText('v', LaneRegions.ChannelVolumeWritten(song, channel).Base)} " +
            $"{PanHeader(song, channel)} " +
            AmplifyHeader(song, channel) +
            SweepHeader(VibratoMap.For(song, channel), VibratoCommandFor) +
            SweepHeader(TremoloMap.For(song, channel), TremoloCommandFor) +
            LegatoHeader(song, channel, shape) +
            HoldHeader(song, channel, shape);
    }

    // The parser's transpose switched off where a channel plays an SMW instrument AddmusicK moves every note of, so every note is written as it sounds,
    // or nothing where it plays none.
    private static string BuiltinTransposeHeader(Song song, int channel)
    {
        bool doesPlayTransposed = PlayedNotes(song, channel).Any(note => song.InstrumentOf(note)!.SmwNumber is int number && MmlBuiltins.TransposeOf(number) != 0);
        return doesPlayTransposed ? BuiltinTransposeOff + " " : "";
    }

    // One channel's block: its banner, the phrases only it plays, its header and its body, the body's lines broken at the line length limit and, where
    // lines are broken by bars, where each group of bars starts.
    private static List<string> ChannelBlock(WrittenChannel channel, int channelIndex, List<LoopCompression.Phrase> phrases, int lineLengthLimit, int? ticksPerBarGroup, WrittenTicks writtenTicks)
    {
        var lines = new List<string> { $";---------------- CHANNEL {Song.ChannelNumberOf(channel.Channel)} ----------------;" };

        var ownPhraseLines = PhraseDirectives(phrases.Where(phrase => !phrase.IsShared && phrase.ChannelIndexes.Contains(channelIndex)), lineLengthLimit).ToList();
        if (ownPhraseLines.Count > 0)
        {
            lines.AddRange(ownPhraseLines);
            lines.Add("");
        }

        lines.AddRange(WrappedLines($"#{channel.Channel} {channel.Header}", lineLengthLimit));

        var bodyTokens = WrittenTokens.Split(channel.Body);
        var bodyParts = ticksPerBarGroup is int barGroupTicks ? BarGroupedTokens(bodyTokens, barGroupTicks, writtenTicks) : [bodyTokens];
        foreach (var bodyPart in bodyParts)
        {
            lines.AddRange(WrappedLines(LoopCompression.WithoutSpacesInsideBrackets(string.Join(' ', bodyPart)), lineLengthLimit));
        }

        return lines;
    }

    // One line of channel data as the lines it is written over, broken in front of the token that would take a line past the limit and never inside
    // one, the lead opening the first line and the indent every line after it.
    private static IEnumerable<string> WrappedLines(string line, int lineLengthLimit, string lead = "", string indent = "")
    {
        var current = new StringBuilder(lead);
        int start = lead.Length;

        foreach (string token in WrittenTokens.Split(line))
        {
            if (current.Length > start && current.Length + 1 + token.Length > lineLengthLimit)
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
    private static IEnumerable<string> PhraseDirectives(IEnumerable<LoopCompression.Phrase> phrases, int lineLengthLimit)
    {
        var phraseList = phrases.ToList();
        int width = LongestLength(phraseList.Select(phrase => phrase.Name));

        return phraseList.SelectMany(phrase =>
        {
            string lead = ReplacementLead(phrase.Name, width);
            return WrappedLines(LoopCompression.WithoutSpacesInsideBrackets(phrase.Text) + QuoteMark, lineLengthLimit, lead, new string(' ', lead.Length));
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
