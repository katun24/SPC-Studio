namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the key-on remote blocks the export writes for runs of notes.
public static partial class MmlExporter
{
    // The commands every note of a run starts with, written once in a key-on remote block armed in front of the run rather than in front of each note.
    private static class KeyOnBlocks
    {
        private static readonly CommandFamily[] FamiliesInBlockOrder =
        [
            CommandFamily.Volume,
            CommandFamily.Pan,
            CommandFamily.Amplify,
            CommandFamily.Vibrato,
            CommandFamily.Tremolo,
            CommandFamily.Noise,
            CommandFamily.Envelope,
        ];

        // Every channel's body with its runs armed, and the blocks themselves, shared by every channel needing the same commands and numbered from the first number in the order they are first used.
        public static (List<string> Bodies, List<string> Definitions) Apply(IReadOnlyList<(string Header, string Body)> channels, bool doesSongLoop, int firstNumber)
        {
            return ChannelsWithBlockRuns<ParsedChannel, BlockRun>(
                channels, firstNumber, ParseChannel, PlanRuns, (channel, runs, numbersByBlock) => WriteChannel(channel, runs, numbersByBlock, doesSongLoop));
        }

        // A channel's tokens and its key-ons, each with the commands in front of it and what each family last wrote before it, the labels the channels
        // before it defined followed out of every call.
        private static ParsedChannel ParseChannel(List<string> headerTokens, List<string> tokens, LabelStatesLeft labelStatesLeft)
        {
            bool hasLoopMarker = tokens.Contains(WrittenTokens.LoopMarker);

            var (beforeLoop, atEnd, _) = WalkFamilyStates(headerTokens, tokens, null);

            // What each family is known to hold where the second pass arrives at the marker, unknown where the two passes differ.
            var afterLoop = FamiliesInBlockOrder.ToDictionary(
                family => family,
                family => beforeLoop.GetValueOrDefault(family) == atEnd.GetValueOrDefault(family) ? beforeLoop.GetValueOrDefault(family) : null);
            var (_, _, statesAtKeyOn) = WalkFamilyStates(headerTokens, tokens, hasLoopMarker ? afterLoop : null);

            var keyOns = KeyOnPlaces(tokens, labelStatesLeft, IsSlotCommand)
                .Select(place => new NoteKeyOn(place.TokenIndex, place.SegmentIndex, MovableTokensInFrontOf(tokens, place.TokenIndex), statesAtKeyOn[place.TokenIndex]))
                .ToList();

            return new ParsedChannel(tokens, keyOns, hasLoopMarker, BaseBlocksAfterEachToken(tokens, labelStatesLeft.BaseBlockByLabel));
        }

        // Where each note outside every arpeggio keys on and which stretch of the channel it is in, a new stretch starting at the marker, at every loop
        // edge, at every token parting the runs and on both sides of a note under an arpeggio, which is left out.
        public static List<(int TokenIndex, int SegmentIndex)> KeyOnPlaces(List<string> tokens, LabelStatesLeft labelStatesLeft, Func<string, bool> doesPartRuns)
        {
            var isArpeggioRunningAfter = ArpeggioRunningAfterEachToken(tokens, labelStatesLeft.IsArpeggioRunningByLabel);
            var places = new List<(int TokenIndex, int SegmentIndex)>();
            int segmentIndex = 0;

            for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
            {
                string token = tokens[tokenIndex];

                if (token == WrittenTokens.LoopMarker || doesPartRuns(token) || WrittenTokens.IsLoopEdge(token))
                {
                    segmentIndex++;
                }

                if (!WrittenTokens.IsNote(token))
                {
                    continue;
                }

                if (isArpeggioRunningAfter[tokenIndex])
                {
                    segmentIndex++;
                    continue;
                }

                places.Add((tokenIndex, segmentIndex));
            }

            return places;
        }

        // What the slot holds outside every run after each token: the legato's key-on block, the envelope reset or both wherever the channel's own arms
        // leave them armed, followed through a label's definition and out of every call of it, and nothing elsewhere.
        private static string?[] BaseBlocksAfterEachToken(List<string> tokens, Dictionary<int, string?> baseBlockByLabel)
        {
            return StatesAfterEachToken(tokens, null, baseBlockByLabel, IsSlotCommand, token => KeyOnSlotBlockCommandsOf(token) is { } commands ? string.Join(' ', commands) : null);
        }

        // Whether an arpeggio runs after each token, followed through a label's definition and out of every call of it.
        public static bool[] ArpeggioRunningAfterEachToken(List<string> tokens, Dictionary<int, bool> isArpeggioRunningByLabel)
        {
            return StatesAfterEachToken(tokens, false, isArpeggioRunningByLabel, token => ArpName.IsNameOf(token) || ArpOffName.IsNameOf(token), ArpName.IsNameOf);
        }

        // What a state of the channel holds after each token, from what it starts at: set by every token that sets it, and followed through a label's
        // definition and out of every call of it, a call of a label whose body never sets it leaving it as it was, and what each body leaves kept for
        // the channels after.
        public static TState[] StatesAfterEachToken<TState>(
            List<string> tokens, TState startState, Dictionary<int, TState> stateLeftByLabel, Func<string, bool> doesSetState, Func<string, TState> stateSetBy)
        {
            var states = new TState[tokens.Count];
            int? openLabel = null;
            var state = startState;

            // Puts a value in the state, as what the label being defined leaves too where one is.
            void SetState(TState value)
            {
                state = value;
                if (openLabel is int labelNumber)
                {
                    stateLeftByLabel[labelNumber] = value;
                }
            }

            for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
            {
                string token = tokens[tokenIndex];
                var label = WrittenTokens.LabelOf(token);

                if (label is { IsDefinition: true })
                {
                    openLabel = label.Value.Number;
                }
                else if (WrittenTokens.IsLabelDefinitionClose(token))
                {
                    openLabel = null;
                }
                else if (doesSetState(token))
                {
                    SetState(stateSetBy(token));
                }
                else if (label is not null && stateLeftByLabel.TryGetValue(label.Value.Number, out var stateLeftByCall))
                {
                    SetState(stateLeftByCall);
                }

                states[tokenIndex] = state;
            }

            return states;
        }

        // Whether a token arms or clears the key-on slot outside every run: the arm of what it holds between runs, its clear, and a held note's clear of
        // both slots.
        private static bool IsSlotCommand(string token)
        {
            return KeyOnSlotBlockCommandsOf(token) is not null || token == KeyOnClearCommand || HoldOffName.IsNameOf(token);
        }

        // The family commands written directly in front of a note, back to the duration, the marker or the loop edge in front of them, as token indexes.
        private static Dictionary<CommandFamily, List<int>> MovableTokensInFrontOf(List<string> tokens, int noteIndex)
        {
            int firstIndex = noteIndex;
            while (firstIndex > 0
                   && WrittenTokens.TicksOf(tokens[firstIndex - 1]) is null
                   && tokens[firstIndex - 1] != WrittenTokens.LoopMarker
                   && !WrittenTokens.IsLoopEdge(tokens[firstIndex - 1]))
            {
                firstIndex--;
            }

            var movableTokens = new Dictionary<CommandFamily, List<int>>();
            for (int tokenIndex = firstIndex; tokenIndex < noteIndex; tokenIndex++)
            {
                var family = FamilyOf(tokens[tokenIndex]);
                if (family == CommandFamily.None)
                {
                    continue;
                }

                if (!movableTokens.TryGetValue(family, out var familyTokens))
                {
                    movableTokens[family] = familyTokens = [];
                }

                familyTokens.Add(tokenIndex);
            }

            return movableTokens;
        }

        // What each family last wrote, walked through the header and the body: the state just in front of the marker, at the end, and in front of every
        // key-on, the second pass arriving at the marker holding what it is given.
        private static (Dictionary<CommandFamily, string?> BeforeLoop, Dictionary<CommandFamily, string?> AtEnd, Dictionary<int, Dictionary<CommandFamily, string?>> AtKeyOn)
            WalkFamilyStates(List<string> headerTokens, List<string> tokens, Dictionary<CommandFamily, string?>? afterLoop)
        {
            var state = new Dictionary<CommandFamily, string?>();
            var beforeLoop = state;
            var atKeyOn = new Dictionary<int, Dictionary<CommandFamily, string?>>();

            foreach (string token in headerTokens)
            {
                RecordFamilyState(state, token);
            }

            for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
            {
                string token = tokens[tokenIndex];

                if (token == WrittenTokens.LoopMarker)
                {
                    beforeLoop = new Dictionary<CommandFamily, string?>(state);
                    if (afterLoop is not null)
                    {
                        state = new Dictionary<CommandFamily, string?>(afterLoop);
                    }

                    continue;
                }

                if (WrittenTokens.IsLoopEdge(token))
                {
                    state.Clear();
                    continue;
                }

                if (WrittenTokens.IsNote(token))
                {
                    atKeyOn[tokenIndex] = new Dictionary<CommandFamily, string?>(state);
                }

                RecordFamilyState(state, token);
            }

            if (ReferenceEquals(beforeLoop, state))
            {
                beforeLoop = new Dictionary<CommandFamily, string?>(state);
            }

            return (beforeLoop, state, atKeyOn);
        }

        // Records a token as what its family last wrote, where it belongs to one.
        private static void RecordFamilyState(Dictionary<CommandFamily, string?> state, string token)
        {
            var family = FamilyOf(token);
            if (family != CommandFamily.None)
            {
                state[family] = token;
            }
        }

        // The runs one channel is worth arming, chosen from the first key-on on, each the one saving the most bytes where any saves anything.
        private static List<BlockRun> PlanRuns(ParsedChannel channel)
        {
            return PlannedRuns(channel.KeyOns.Count, keyOnIndex => BestRunFrom(channel, keyOnIndex), run => run.LastKeyOn);
        }

        // The run starting at a key-on that saves the most bytes, trying every combination of the families in front of it as a block.
        private static BlockRun? BestRunFrom(ParsedChannel channel, int firstKeyOn)
        {
            var keyOns = channel.KeyOns;
            var presentFamilies = FamiliesInBlockOrder.Where(keyOns[firstKeyOn].MovableTokensByFamily.ContainsKey).ToArray();
            string[] baseCommands = channel.BaseBlockBefore(keyOns[firstKeyOn].TokenIndex)?.Split(' ') ?? [];
            BlockRun? bestRun = null;

            for (int familyMask = 1; familyMask < 1 << presentFamilies.Length; familyMask++)
            {
                var families = presentFamilies.Where((_, bit) => (familyMask & (1 << bit)) != 0).ToArray();
                var blockByFamily = families.ToDictionary(
                    family => family,
                    family => keyOns[firstKeyOn].MovableTokensByFamily[family].Select(tokenIndex => channel.Tokens[tokenIndex]).ToList());

                var (lastKeyOn, bytesSaved) = RunExtent(keyOns, firstKeyOn, keyOn => keyOn.SegmentIndex, keyOn => BytesSavedAt(channel, keyOn, blockByFamily));
                int bytesSavedAfterArmAndClear = bytesSaved - ArmAndClearCount * SongDataSize.RemoteCallBytes;

                if (bestRun is null || bytesSavedAfterArmAndClear > bestRun.NetBytesSaved)
                {
                    var blockCommands = families.SelectMany(family => blockByFamily[family]).Concat(baseCommands).ToArray();
                    bestRun = new BlockRun(firstKeyOn, lastKeyOn, blockCommands, families, bytesSavedAfterArmAndClear);
                }
            }

            return bestRun;
        }

        // The bytes a key-on stops writing if the block runs at it, or null where the block would change what it plays.
        private static int? BytesSavedAt(ParsedChannel channel, NoteKeyOn keyOn, Dictionary<CommandFamily, List<string>> blockByFamily)
        {
            int bytesSaved = 0;

            foreach (var (family, blockCommands) in blockByFamily)
            {
                var writtenCommands = keyOn.MovableTokensByFamily.TryGetValue(family, out var tokenIndexes)
                    ? tokenIndexes.Select(tokenIndex => channel.Tokens[tokenIndex]).ToList()
                    : [];

                if (writtenCommands.SequenceEqual(blockCommands))
                {
                    bytesSaved += writtenCommands.Sum(WrittenTokens.CompiledBytesOf);
                    continue;
                }

                // Whether the block would only say again what is already in force, the note writing nothing of the family.
                bool doesRestate = writtenCommands.Count == 0
                                   && blockCommands.Count == 1
                                   && IsPlainSet(blockCommands[0])
                                   && keyOn.StateBefore.GetValueOrDefault(family) == blockCommands[0];

                if (!doesRestate)
                {
                    return null;
                }
            }

            return bytesSaved;
        }

        // The channel with its runs armed: each run's commands taken out from in front of its notes, an arm where a run starts, and the slot put back to
        // what it holds outside every run where a note outside every run follows one and in front of every loop edge.
        private static string WriteChannel(ParsedChannel channel, List<BlockRun> runs, Dictionary<string, int> numbersByBlock, bool doesSongLoop)
        {
            var (runByNoteToken, removedTokens) = RunsByNoteToken(channel, runs);
            var (armedAtMarker, armedAtEnd) = ArmedBlocksAtMarkerAndEnd(channel, runByNoteToken);
            string? armedBlock = null;
            var output = new List<string>();

            // Whether what the slot holds is unknown here: at the start of a looping channel with no marker that ends with a block armed.
            bool isArmedUnknown = doesSongLoop && !channel.HasLoopMarker && armedAtEnd is not null;

            // Whether the slot has to be armed in front of a token for it to hold a block.
            bool IsArmingNeededFor(string? block)
            {
                return isArmedUnknown || block != armedBlock;
            }

            for (int tokenIndex = 0; tokenIndex < channel.Tokens.Count; tokenIndex++)
            {
                string token = channel.Tokens[tokenIndex];
                if (removedTokens.Contains(tokenIndex))
                {
                    continue;
                }

                if (token == WrittenTokens.LoopMarker)
                {
                    output.Add(token);
                    isArmedUnknown = armedAtMarker != armedAtEnd;
                    armedBlock = armedAtMarker;
                    continue;
                }

                if (WrittenTokens.IsLoopEdge(token) && IsArmingNeededFor(channel.BaseBlockBefore(tokenIndex)))
                {
                    output.Add(CommandArming(channel.BaseBlockBefore(tokenIndex), numbersByBlock));
                }

                if (TryGetArmedAfter(channel, runByNoteToken, tokenIndex, out string? armedAfter))
                {
                    if (WrittenTokens.IsNote(token) && IsArmingNeededFor(armedAfter))
                    {
                        InsertInFrontOfNote(output, CommandArming(armedAfter, numbersByBlock));
                    }

                    armedBlock = armedAfter;
                    isArmedUnknown = false;
                }

                output.Add(token);
            }

            return string.Join(' ', output);
        }

        // The run each note's token belongs to, and the tokens the runs take out from in front of their notes.
        private static (Dictionary<int, BlockRun?> RunByNoteToken, HashSet<int> RemovedTokens) RunsByNoteToken(ParsedChannel channel, List<BlockRun> runs)
        {
            var keyOns = channel.KeyOns;
            var runAtKeyOn = new BlockRun?[keyOns.Count];
            var removedTokens = new HashSet<int>();

            foreach (var run in runs)
            {
                for (int keyOnIndex = run.FirstKeyOn; keyOnIndex <= run.LastKeyOn; keyOnIndex++)
                {
                    runAtKeyOn[keyOnIndex] = run;

                    foreach (var family in run.Families)
                    {
                        if (keyOns[keyOnIndex].MovableTokensByFamily.TryGetValue(family, out var tokenIndexes))
                        {
                            removedTokens.UnionWith(tokenIndexes);
                        }
                    }
                }
            }

            var runByNoteToken = keyOns.Select((keyOn, keyOnIndex) => (keyOn.TokenIndex, Run: runAtKeyOn[keyOnIndex])).ToDictionary(pair => pair.TokenIndex, pair => pair.Run);
            return (runByNoteToken, removedTokens);
        }

        // What the slot holds where the first pass reaches the marker and where the channel ends, which is what the second pass arrives at the marker holding.
        private static (string? AtMarker, string? AtEnd) ArmedBlocksAtMarkerAndEnd(ParsedChannel channel, Dictionary<int, BlockRun?> runByNoteToken)
        {
            string? armedBlock = null;
            string? armedAtMarker = null;

            for (int tokenIndex = 0; tokenIndex < channel.Tokens.Count; tokenIndex++)
            {
                string token = channel.Tokens[tokenIndex];

                if (token == WrittenTokens.LoopMarker)
                {
                    armedAtMarker = armedBlock;
                }
                else if (TryGetArmedAfter(channel, runByNoteToken, tokenIndex, out string? armedAfter))
                {
                    armedBlock = armedAfter;
                }
            }

            return (armedAtMarker, armedBlock);
        }

        // What the slot holds after a token that puts something in it - what it holds outside every run after an arm, a clear or a loop edge, and
        // the block a note keys on under - and whether the token is one of those.
        private static bool TryGetArmedAfter(ParsedChannel channel, Dictionary<int, BlockRun?> runByNoteToken, int tokenIndex, out string? armedAfter)
        {
            string token = channel.Tokens[tokenIndex];

            if (IsSlotCommand(token) || WrittenTokens.IsLoopEdge(token))
            {
                armedAfter = channel.BaseBlocksAfter[tokenIndex];
                return true;
            }

            if (WrittenTokens.IsNote(token))
            {
                armedAfter = BlockWantedAt(channel, runByNoteToken, tokenIndex);
                return true;
            }

            armedAfter = null;
            return false;
        }

        // Puts a command in front of the note about to be written, and in front of the octave the note is written under rather than between the two.
        public static void InsertInFrontOfNote(List<string> output, string command)
        {
            int insertAt = output.Count > 0 && WrittenTokens.IsOctave(output[^1]) ? output.Count - 1 : output.Count;
            output.Insert(insertAt, command);
        }

        // The block a note keys on under: its run's, or what the slot holds outside every run where it is in none.
        private static string? BlockWantedAt(ParsedChannel channel, Dictionary<int, BlockRun?> runByNoteToken, int noteTokenIndex)
        {
            return runByNoteToken.GetValueOrDefault(noteTokenIndex)?.BlockText ?? channel.BaseBlockBefore(noteTokenIndex);
        }

        // The command that puts a block in the slot: a run's block's arm by its number, the arm of what the slot holds between runs, or the clear where
        // it is none.
        private static string CommandArming(string? block, Dictionary<string, int> numbersByBlock)
        {
            if (block is null)
            {
                return KeyOnClearCommand;
            }

            if (numbersByBlock.TryGetValue(block, out int blockNumber))
            {
                return KeyOnArmCommandFor(blockNumber);
            }

            return KeyOnSlotPlaceholderFor(block) ?? throw new InvalidOperationException("A key-on block that is neither a run's nor the slot's between runs has no arm.");
        }

        // Which family a command belongs to, or none.
        public static CommandFamily FamilyOf(string token)
        {
            if (WrittenTokens.IsLettered(token, 'v'))
            {
                return CommandFamily.Volume;
            }

            if (WrittenTokens.IsLettered(token, 'y') || PanFadeName.IsNameOf(token))
            {
                return CommandFamily.Pan;
            }

            if (AmpName.IsNameOf(token))
            {
                return CommandFamily.Amplify;
            }

            if (WrittenTokens.IsLettered(token, 'p') || VibFadeName.IsNameOf(token) || VibOffName.IsNameOf(token))
            {
                return CommandFamily.Vibrato;
            }

            if (TremName.IsNameOf(token) || TremOffName.IsNameOf(token))
            {
                return CommandFamily.Tremolo;
            }

            if (WrittenTokens.IsHexLettered(token, 'n'))
            {
                return CommandFamily.Noise;
            }

            if (EnvName.IsNameOf(token) || EnvResetName.IsNameOf(token))
            {
                return CommandFamily.Envelope;
            }

            return CommandFamily.None;
        }

        // Whether stating a command again where it is already in force changes nothing: a plain set, rather than a fade or the noise clock.
        private static bool IsPlainSet(string token)
        {
            return FamilyOf(token) switch
            {
                CommandFamily.Volume => !token.Contains(','),
                CommandFamily.Pan => WrittenTokens.IsLettered(token, 'y'),
                CommandFamily.Amplify => true,
                CommandFamily.Vibrato => WrittenTokens.IsLettered(token, 'p') || VibOffName.IsNameOf(token),
                CommandFamily.Tremolo => true,
                _ => false,
            };
        }

        // The kinds of command a block may hold, each moved as a whole or not at all.
        public enum CommandFamily
        {
            None,
            Volume,
            Pan,
            Amplify,
            Vibrato,
            Tremolo,
            Noise,
            Envelope,
        }

        // One note's key-on as the stream has it: where its first piece is, which stretch of the channel it is in, the family commands in front of it, and what each family last wrote before it.
        private sealed record NoteKeyOn(int TokenIndex, int SegmentIndex, Dictionary<CommandFamily, List<int>> MovableTokensByFamily, Dictionary<CommandFamily, string?> StateBefore);

        // A channel's tokens, the key-ons among them, whether it has a loop marker, and what the slot holds outside every run after each token.
        private sealed record ParsedChannel(List<string> Tokens, List<NoteKeyOn> KeyOns, bool HasLoopMarker, string?[] BaseBlocksAfter)
        {
            // What the slot holds outside every run in front of a token.
            public string? BaseBlockBefore(int tokenIndex)
            {
                return tokenIndex > 0 ? BaseBlocksAfter[tokenIndex - 1] : null;
            }
        }

        // What each label's body leaves the key-on slot, the arpeggio and the ordinary slot at, kept over the channels in the order they are written,
        // where the body sets them.
        public sealed class LabelStatesLeft
        {
            public readonly Dictionary<int, string?> BaseBlockByLabel = [];
            public readonly Dictionary<int, bool> IsArpeggioRunningByLabel = [];
            public readonly Dictionary<int, bool> IsKeyOffBlockArmedByLabel = [];
        }

        // A run of consecutive key-ons that all start with the block's commands, and the bytes it saves once its own arm and clear are paid for.
        private sealed record BlockRun(int FirstKeyOn, int LastKeyOn, string[] BlockCommands, CommandFamily[] Families, int NetBytesSaved) : IBlockRun
        {
            // The block's commands as one text, which is what tells two blocks apart.
            public string BlockText
            {
                get
                {
                    return string.Join(' ', BlockCommands);
                }
            }
        }
    }
}
