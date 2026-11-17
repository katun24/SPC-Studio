using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the timer remote blocks the export writes for runs of notes.
public static partial class MmlExporter
{
    // The commands every note of a run writes at the same point inside itself, written once in a timer remote block armed in front of the run, each
    // note then written whole.
    private static class TimerBlocks
    {
        private const string TimerClearCommand = "(!!1)";
        private const int FirstCountedTicks = 1;
        private const int LastCountedTicks = byte.MaxValue;

        private static readonly int[] WaitTimes = [SnesTiming.DefaultWaitTime, SnesTiming.StaccatoWaitTime];
        private static readonly int[] EveryGate = [.. Enumerable.Range(SnesTiming.MinGate, SnesTiming.MaxGate - SnesTiming.MinGate + 1)];

        // Every channel's body with its runs armed, and the blocks themselves, shared by every channel needing the same commands and numbered from the
        // first number in the order they are first used.
        public static (List<string> Bodies, List<string> Definitions) Apply(IReadOnlyList<(string Header, string Body)> channels, bool doesSongLoop, int firstNumber)
        {
            return ChannelsWithBlockRuns<ParsedChannel, TimerRun>(
                channels, firstNumber, (headerTokens, tokens, labelStatesLeft) => ParseChannel(headerTokens, tokens, labelStatesLeft, doesSongLoop), PlanRuns, WriteChannel);
        }

        // A channel's tokens and its key-ons, each with the break inside its note a timer block could take, the labels the channels before it defined
        // followed out of every call.
        private static ParsedChannel ParseChannel(List<string> headerTokens, List<string> tokens, KeyOnBlocks.LabelStatesLeft labelStatesLeft, bool doesSongLoop)
        {
            var isSlotFreeBefore = OrdinarySlotFreeBeforeEachToken(headerTokens, tokens, labelStatesLeft.IsKeyOffBlockArmedByLabel, doesSongLoop);
            var gateBefore = GatesBeforeEachToken(headerTokens, tokens);
            var places = KeyOnBlocks.KeyOnPlaces(tokens, labelStatesLeft, _ => false);
            var keyOns = new List<TimedKeyOn>();

            for (int placeIndex = 0; placeIndex < places.Count; placeIndex++)
            {
                var (tokenIndex, segmentIndex) = places[placeIndex];
                int? nextKeyOnIndex = placeIndex + 1 < places.Count && places[placeIndex + 1].SegmentIndex == segmentIndex ? places[placeIndex + 1].TokenIndex : null;
                var noteBreak = isSlotFreeBefore[tokenIndex] ? BreakInNote(tokens, tokenIndex, nextKeyOnIndex, gateBefore[tokenIndex]) : null;
                keyOns.Add(new TimedKeyOn(tokenIndex, segmentIndex, noteBreak));
            }

            return new ParsedChannel(tokens, keyOns);
        }

        // Whether the ordinary slot is known to hold nothing in front of each token: no key-off block armed by a held note or a release run, followed
        // through a label's definition and out of every call of it, and on every pass - unknown past the marker, or over a looping channel with no
        // marker, where the second pass arrives holding something else.
        private static bool[] OrdinarySlotFreeBeforeEachToken(List<string> headerTokens, List<string> tokens, Dictionary<int, bool> isArmedLeftByLabel, bool doesSongLoop)
        {
            bool isArmedAtStart = headerTokens.Where(IsOrdinarySlotCommand).Select(IsKeyOffArmCommand).LastOrDefault();
            var isArmedAfter = KeyOnBlocks.StatesAfterEachToken(tokens, isArmedAtStart, isArmedLeftByLabel, IsOrdinarySlotCommand, IsKeyOffArmCommand);
            bool isArmedAtEnd = tokens.Count > 0 ? isArmedAfter[^1] : isArmedAtStart;
            int markerIndex = tokens.IndexOf(WrittenTokens.LoopMarker);

            // The first token from which the slot is unknown, past the end where it is known throughout.
            int firstUnknownIndex = tokens.Count;
            if (markerIndex >= 0 && isArmedAfter[markerIndex] != isArmedAtEnd)
            {
                firstUnknownIndex = markerIndex;
            }
            else if (markerIndex < 0 && doesSongLoop && isArmedAtStart != isArmedAtEnd)
            {
                firstUnknownIndex = 0;
            }

            return [.. Enumerable.Range(0, tokens.Count).Select(tokenIndex => tokenIndex < firstUnknownIndex && !(tokenIndex > 0 ? isArmedAfter[tokenIndex - 1] : isArmedAtStart))];
        }

        // Whether a token arms or clears the ordinary slot: a key-off block's arm, or the clear of both slots.
        private static bool IsOrdinarySlotCommand(string token)
        {
            return IsKeyOffArmCommand(token) || HoldOffName.IsNameOf(token);
        }

        // The q gate the driver keys each token's note on under, read off the q written last as the parser does, or null past the marker until a q is
        // written, the second pass arriving under whatever q the channel's end left.
        private static int?[] GatesBeforeEachToken(List<string> headerTokens, List<string> tokens)
        {
            int? quantization = headerTokens.Where(WrittenTokens.IsQ).Select(QuantizationOf).DefaultIfEmpty(WrittenTokens.ChannelStartQ).Last();
            var gates = new int?[tokens.Count];

            for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
            {
                string token = tokens[tokenIndex];
                gates[tokenIndex] = quantization is int known ? Note.GateOfQuantization(known) : null;

                if (token == WrittenTokens.LoopMarker)
                {
                    quantization = null;
                }
                else if (WrittenTokens.IsQ(token))
                {
                    quantization = QuantizationOf(token);
                }
            }

            return gates;
        }

        // The q byte a written q states.
        private static int QuantizationOf(string qToken)
        {
            return Convert.ToInt32(qToken[1..], HexRadix);
        }

        // The break inside the note at a token a timer block can take, or null where it has none: the note's first piece and the ties straight behind it,
        // then nothing but commands a block may hold, then ties to the note's end - the timer firing on the commands' own tick, the note written whole
        // keyed off where its pieces are, and no second fire before the next key-on or, where none follows in its stretch, the note's end.
        private static NoteBreak? BreakInNote(List<string> tokens, int noteIndex, int? nextKeyOnIndex, int? gate)
        {
            int commandStart = AfterTies(tokens, noteIndex + 1);
            int tailStart = commandStart;
            while (tailStart < tokens.Count && WrittenTokens.TicksOf(tokens[tailStart]) is null && KeyOnBlocks.FamilyOf(tokens[tailStart]) != KeyOnBlocks.CommandFamily.None)
            {
                tailStart++;
            }

            if (tailStart == commandStart || tailStart >= tokens.Count || !WrittenTokens.IsTie(tokens[tailStart]))
            {
                return null;
            }

            int noteEnd = AfterTies(tokens, tailStart);
            if (NextDurationIndex(tokens, noteEnd) is int nextDuration && WrittenTokens.IsTie(tokens[nextDuration]))
            {
                return null;
            }

            int breakTicks = TicksBetween(tokens, noteIndex, commandStart);
            int tailTicks = TicksBetween(tokens, tailStart, noteEnd);
            int noteTicks = breakTicks + tailTicks;
            var wholePieces = MmlDurationEncoding.SplitRunIntoTiedPieces(noteTicks);
            if (CountedTicksFiringAt(wholePieces, breakTicks) is not int countedTicks || !DoesWholeNoteKeyOffAlike(noteTicks, tailTicks, gate))
            {
                return null;
            }

            int ticksToNextKeyOn = nextKeyOnIndex is int nextIndex ? TicksBetween(tokens, noteIndex, nextIndex) : noteTicks;
            if (ticksToNextKeyOn > countedTicks + MmlRemoteCall.TimerPeriod)
            {
                return null;
            }

            var commands = tokens[commandStart..tailStart];
            int durationInForceSplit = MmlDurationEncoding.NoDurationTicks;
            int bytesSplit = MmlDurationEncoding.EstimatedRunBytes(breakTicks, ref durationInForceSplit)
                             + commands.Sum(WrittenTokens.CompiledBytesOf)
                             + MmlDurationEncoding.EstimatedRunBytes(tailTicks, ref durationInForceSplit);
            int durationInForceWhole = MmlDurationEncoding.NoDurationTicks;
            int bytesWhole = MmlDurationEncoding.EstimatedRunBytes(noteTicks, ref durationInForceWhole);
            string wholeNote = WrittenTokens.RunOfLength(WrittenTokens.RunSpellingOf(tokens[noteIndex]), noteTicks);

            return new NoteBreak(string.Join(' ', commands), countedTicks, wholeNote, noteEnd - 1, bytesSplit - bytesWhole);
        }

        // The index past the ties standing straight from an index on.
        private static int AfterTies(List<string> tokens, int firstIndex)
        {
            int index = firstIndex;
            while (index < tokens.Count && WrittenTokens.IsTie(tokens[index]))
            {
                index++;
            }

            return index;
        }

        // The index of the first note, rest or tie from an index on, or null where none follows.
        private static int? NextDurationIndex(List<string> tokens, int firstIndex)
        {
            for (int index = firstIndex; index < tokens.Count; index++)
            {
                if (WrittenTokens.TicksOf(tokens[index]) is not null)
                {
                    return index;
                }
            }

            return null;
        }

        // The ticks the notes, rests and ties from one index up to another last.
        private static int TicksBetween(List<string> tokens, int firstIndex, int endIndex)
        {
            return tokens[firstIndex..endIndex].Sum(token => WrittenTokens.TicksOf(token) ?? 0);
        }

        // The count a timer block is armed with to fire that many ticks into a note written as the pieces, or null where no count does: the tick
        // fetches a byte, which the timer does not count.
        private static int? CountedTicksFiringAt(IReadOnlyList<int> pieces, int fireTick)
        {
            for (int countedTicks = FirstCountedTicks; countedTicks <= LastCountedTicks; countedTicks++)
            {
                var tick = SnesTiming.TickAfterCountedTicks(0, pieces, countedTicks);
                if (tick == fireTick)
                {
                    return countedTicks;
                }

                if (tick is null || tick > fireTick)
                {
                    return null;
                }
            }

            return null;
        }

        // Whether a note written whole is keyed on as long as written with its tail tied on, under its gate or every gate where it is not known, and
        // under either wait time, the light staccato being the song's.
        private static bool DoesWholeNoteKeyOffAlike(int noteTicks, int tailTicks, int? gate)
        {
            int[] gates = gate is int knownGate ? [knownGate] : EveryGate;
            return gates.All(eachGate => WaitTimes.All(waitTime =>
                SnesTiming.KeyOnTicks(noteTicks, eachGate, tailTicks, waitTime) == SnesTiming.KeyOnTicks(noteTicks, eachGate, null, waitTime)));
        }

        // The runs one channel is worth arming, chosen from the first key-on on.
        private static List<TimerRun> PlanRuns(ParsedChannel channel)
        {
            return PlannedRuns(channel.KeyOns.Count, keyOnIndex => RunFrom(channel, keyOnIndex), run => run.LastKeyOn);
        }

        // The run starting at a key-on: every key-on from it on in its stretch whose note breaks with the same commands at the same count, or null where
        // its note has no break a block can take.
        private static TimerRun? RunFrom(ParsedChannel channel, int firstKeyOn)
        {
            if (channel.KeyOns[firstKeyOn].Break is not { } firstBreak)
            {
                return null;
            }

            var (lastKeyOn, bytesSaved) = RunExtent(
                channel.KeyOns,
                firstKeyOn,
                keyOn => keyOn.SegmentIndex,
                keyOn => keyOn.Break is { } noteBreak && noteBreak.BlockText == firstBreak.BlockText && noteBreak.CountedTicks == firstBreak.CountedTicks ? noteBreak.BytesSaved : null);

            return new TimerRun(firstKeyOn, lastKeyOn, firstBreak.BlockText, firstBreak.CountedTicks, bytesSaved - ArmAndClearCount * SongDataSize.RemoteCallBytes);
        }

        // The channel with its runs armed: each run's notes written whole, the arm in front of its first and the clear behind its last.
        private static string WriteChannel(ParsedChannel channel, List<TimerRun> runs, Dictionary<string, int> numbersByBlock)
        {
            var runByKeyOn = runs.SelectMany(run => Enumerable.Range(run.FirstKeyOn, run.LastKeyOn - run.FirstKeyOn + 1).Select(keyOnIndex => (keyOnIndex, run)))
                .ToDictionary(pair => channel.KeyOns[pair.keyOnIndex].TokenIndex, pair => (KeyOnIndex: pair.keyOnIndex, Run: pair.run));
            var output = new List<string>();

            for (int tokenIndex = 0; tokenIndex < channel.Tokens.Count; tokenIndex++)
            {
                if (!runByKeyOn.TryGetValue(tokenIndex, out var member))
                {
                    output.Add(channel.Tokens[tokenIndex]);
                    continue;
                }

                var noteBreak = channel.KeyOns[member.KeyOnIndex].Break!;
                if (member.KeyOnIndex == member.Run.FirstKeyOn)
                {
                    KeyOnBlocks.InsertInFrontOfNote(output, TimerArmCommandFor(numbersByBlock[member.Run.BlockText], member.Run.CountedTicks));
                }

                output.Add(noteBreak.WholeNote);
                if (member.KeyOnIndex == member.Run.LastKeyOn)
                {
                    output.Add(TimerClearCommand);
                }

                tokenIndex = noteBreak.LastTokenIndex;
            }

            return string.Join(' ', output);
        }

        // A channel's tokens and its key-ons.
        private sealed record ParsedChannel(List<string> Tokens, List<TimedKeyOn> KeyOns);

        // One note's key-on as the stream has it: where its first piece is, which stretch of the channel it is in, and the break inside it a timer block
        // could take, or null where it has none.
        private sealed record TimedKeyOn(int TokenIndex, int SegmentIndex, NoteBreak? Break);

        // A break inside a note a timer block can take: its commands as one text, the count the block fires at, the note written whole, the index of the
        // note's last tie, and the bytes writing it whole saves.
        private sealed record NoteBreak(string BlockText, int CountedTicks, string WholeNote, int LastTokenIndex, int BytesSaved);

        // A run of consecutive key-ons whose notes all break with the block's commands at the same count, and the bytes it saves once its own arm and
        // clear are paid for.
        private sealed record TimerRun(int FirstKeyOn, int LastKeyOn, string BlockText, int CountedTicks, int NetBytesSaved) : IBlockRun;
    }
}
