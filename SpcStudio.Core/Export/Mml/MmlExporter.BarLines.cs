namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the parting of a channel's body where its groups of bars pass.
public static partial class MmlExporter
{
    // A channel body's tokens parted where each group of bars starts, the commands written in front of the first piece to start in a later group going
    // with it, and a loop placed by the tick it starts at.
    private static List<List<string>> BarGroupedTokens(List<string> tokens, int ticksPerBarGroup, WrittenTicks writtenTicks)
    {
        var breakIndexes = new List<int>();
        int currentBarGroup = 0;
        int? commandRunStart = null;

        foreach (var piece in writtenTicks.PiecesOf(tokens))
        {
            if (piece.Ticks == 0)
            {
                commandRunStart ??= piece.FirstIndex;
                continue;
            }

            int barGroup = piece.StartTick / ticksPerBarGroup;
            int breakIndex = commandRunStart ?? piece.FirstIndex;
            commandRunStart = null;

            if (barGroup > currentBarGroup && breakIndex > 0)
            {
                breakIndexes.Add(breakIndex);
            }

            currentBarGroup = Math.Max(currentBarGroup, barGroup);
        }

        return TokensPartedAt(tokens, breakIndexes);
    }

    // Tokens parted in front of each of the indexes given, in order.
    private static List<List<string>> TokensPartedAt(List<string> tokens, List<int> breakIndexes)
    {
        var parts = new List<List<string>>();
        int partStart = 0;

        foreach (int breakIndex in breakIndexes.Append(tokens.Count))
        {
            parts.Add(tokens.GetRange(partStart, breakIndex - partStart));
            partStart = breakIndex;
        }

        return parts;
    }

    // One piece at the top of a run of written tokens - a token, or a loop from its opening to its close - with the tick it starts at and the ticks it
    // plays.
    private readonly record struct WrittenPiece(int FirstIndex, int StartTick, int Ticks);

    // The ticks the channel text the export has written plays, each loop counted as often as it plays and each label's call and phrase as its body.
    private sealed class WrittenTicks
    {
        private readonly Dictionary<int, string> _textsByLabel;
        private readonly Dictionary<string, int> _labelsByPhraseName;
        private readonly Dictionary<int, int> _ticksByLabel = [];

        // Reads the text with the phrases the labels' bodies were taken out into.
        public WrittenTicks(IReadOnlyList<LoopCompression.Phrase> phrases)
        {
            _textsByLabel = phrases.ToDictionary(phrase => phrase.LabelNumber, phrase => phrase.Text);
            _labelsByPhraseName = phrases.ToDictionary(phrase => phrase.Name, phrase => phrase.LabelNumber);
        }

        // Each piece at the top of a run of tokens, in order.
        public List<WrittenPiece> PiecesOf(IReadOnlyList<string> tokens)
        {
            var pieces = new List<WrittenPiece>();
            int startTick = 0;

            for (int firstIndex = 0; firstIndex < tokens.Count; firstIndex++)
            {
                bool isLoop = WrittenTokens.IsLoopOpening(tokens[firstIndex]);
                int lastIndex = isLoop ? WrittenTokens.LoopEndIndex(tokens, firstIndex) : firstIndex;
                int pieceTicks = isLoop ? LoopTicks(tokens, firstIndex, lastIndex) : TokenTicks(tokens[firstIndex]);

                pieces.Add(new WrittenPiece(firstIndex, startTick, pieceTicks));
                startTick += pieceTicks;
                firstIndex = lastIndex;
            }

            return pieces;
        }

        // The ticks a loop plays: its body as many times as its close says.
        private int LoopTicks(IReadOnlyList<string> tokens, int openIndex, int closeIndex)
        {
            var bodyTokens = tokens.Skip(openIndex + 1).Take(closeIndex - openIndex - 1).ToList();
            return RunTicks(bodyTokens) * WrittenTokens.LoopCountOf(tokens[closeIndex]);
        }

        // The ticks a run of tokens plays.
        private int RunTicks(IReadOnlyList<string> tokens)
        {
            return PiecesOf(tokens).Sum(piece => piece.Ticks);
        }

        // The ticks one token plays: a label's call its body as many times as the call says, a phrase its label's body, a note, rest or tie its length,
        // and a command none.
        private int TokenTicks(string token)
        {
            if (WrittenTokens.LabelOf(token) is (int calledLabel, false))
            {
                return LabelTicks(calledLabel) * WrittenTokens.LoopCountOf(token);
            }

            if (_labelsByPhraseName.TryGetValue(token, out int phraseLabel))
            {
                return LabelTicks(phraseLabel);
            }

            return WrittenTokens.TicksOf(token) ?? 0;
        }

        // The ticks a label's body plays once, read off its phrase the first time it is asked.
        private int LabelTicks(int labelNumber)
        {
            if (_ticksByLabel.TryGetValue(labelNumber, out int knownTicks))
            {
                return knownTicks;
            }

            int labelTicks = RunTicks(WrittenTokens.Split(_textsByLabel[labelNumber]));
            _ticksByLabel[labelNumber] = labelTicks;
            return labelTicks;
        }
    }
}
