using System.Text;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the loop pass: repeated phrases written once as label loops and subloops.
public static partial class MmlExporter
{
    // Repeated phrases written once and played again: a phrase used in more than one place becomes a label loop, and repeats directly following each other become a subloop.
    private static partial class LoopCompression
    {
        private const int SubloopEdgeCount = 2;
        private const int EdgeRestatementBytes = 2;
        private const int LabelDefinitionBytes = SongDataSize.LoopEndBytes + EdgeRestatementBytes;
        private const int NoShift = 0;
        private const int MaxSubloopUnitEvents = 64;
        private const int MaxLoopCount = byte.MaxValue;
        private const int MinSubloopCount = 2;
        private const int NoId = -1;
        private const int LowestStatedOctave = 1;
        private const int HighestStatedOctave = 6;
        private const int SortKeyRankShift = 32;
        private const string LegatoBitSetKeyMark = "L";
        private const string NotePieceKeyMark = "#";
        private const string NoteValueKeyMark = "=";
        private const string IntervalKeyMark = "~";
        private const int TransposesAroundShiftedCall = 2;

        private static readonly int SubloopBytes = SubloopEdgeCount * SongDataSize.SubloopEdgeBytes;
        private static readonly int ShiftedCallTransposeBytes = TransposesAroundShiftedCall * WrittenTokens.CompiledBytesOf(TransposeToken(0));

        // Repeats written as a subloop, which holds events and the calls of labels holding no subloop.
        private static readonly RepeatLoopKind AsSubloop = new(SubloopKeyOf, SubloopBytesOver, (unitItems, count) => new SubloopItem(unitItems, count));

        // Every channel's body with its loops written under the options, those at the indexes given looping inside their release runs too but behind
        // the notes barred there, and how many labels they number from 1: a copy played shifted by whole semitones called under an $FA $02 where the
        // options name it and that saves bytes, and each long note, rest or tie left outside every subloop written with one where they name it and that
        // saves bytes.
        public static (List<string> Bodies, int LabelCount) Apply(
            IReadOnlyList<(string Header, string Body)> channels, IReadOnlyDictionary<int, IReadOnlySet<int>> barredReleaseNotesByChannel, MmlExportOptions options)
        {
            var segmentsByChannel = ParseChannels(channels, barredReleaseNotesByChannel);
            var segments = segmentsByChannel.SelectMany(channelSegments => channelSegments).ToList();

            FindLabels(segments, options.TransposedLoops ? LabelFinder.MatchingShiftedCopies(segments) : LabelFinder.MatchingExactCopies(segments));

            foreach (var label in segments.SelectMany(LabelsCalledIn).Distinct().ToList())
            {
                label.BodyItems = WithSubloops(label.BodyItems);
            }

            TakeSegmentLoops(segments);

            if (options.AreLongNotesAndRestsWrittenAsLoops())
            {
                WriteLongRunsAsLoops(segments);
            }

            return WriteChannels(channels, segmentsByChannel);
        }

        // The bytes copies of a unit cost written as one subloop, as the subloop search prices it: the unit once, the subloop's two edges and the
        // restatements behind them.
        public static int SubloopBytesOver(int unitBytes)
        {
            return unitBytes + SubloopBytes + 2 * EdgeRestatementBytes;
        }

        // A body with no space inside a loop's brackets, two brackets side by side keeping theirs.
        public static string WithoutSpacesInsideBrackets(string body)
        {
            var tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var text = new StringBuilder();

            for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
            {
                if (tokenIndex > 0)
                {
                    string tokenBefore = tokens[tokenIndex - 1];
                    string token = tokens[tokenIndex];
                    bool isAfterOpening = tokenBefore.EndsWith('[') && token[0] != '[';
                    bool isClosing = token[0] == ']' && tokenBefore[0] != ']';

                    if (!isAfterOpening && !isClosing)
                    {
                        text.Append(' ');
                    }
                }

                text.Append(tokens[tokenIndex]);
            }

            return text.ToString();
        }

        // Every channel's body with each label's body taken out into a phrase, in label order.
        public static (List<string> Bodies, List<Phrase> Phrases) WithPhrasesNamed(IReadOnlyList<string> bodies)
        {
            var textsByLabel = new SortedDictionary<int, string>();
            var channelsByLabel = new Dictionary<int, HashSet<int>>();
            var namedBodies = new List<string>();

            for (int channelIndex = 0; channelIndex < bodies.Count; channelIndex++)
            {
                var tokens = bodies[channelIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var output = new List<string>();

                for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
                {
                    output.Add(tokens[tokenIndex]);

                    if (WrittenTokens.LabelOf(tokens[tokenIndex]) is not var (labelNumber, isDefinition))
                    {
                        continue;
                    }

                    if (!channelsByLabel.TryGetValue(labelNumber, out var channels))
                    {
                        channelsByLabel[labelNumber] = channels = [];
                    }

                    channels.Add(channelIndex);

                    if (!isDefinition)
                    {
                        continue;
                    }

                    int endIndex = WrittenTokens.LoopEndIndex(tokens, tokenIndex);
                    textsByLabel[labelNumber] = string.Join(' ', tokens[(tokenIndex + 1)..endIndex]);
                    output.Add(Phrase.NameFor(labelNumber));
                    output.Add(tokens[endIndex]);
                    tokenIndex = endIndex;
                }

                namedBodies.Add(string.Join(' ', output));
            }

            var phrases = textsByLabel.Select(pair => new Phrase(pair.Key, pair.Value, channelsByLabel[pair.Key])).ToList();
            return (namedBodies, phrases);
        }

        // The labels a segment calls, directly or from inside a subloop.
        private static IEnumerable<Label> LabelsCalledIn(Segment segment)
        {
            return segment.Items.SelectMany(LabelsCalledBy);
        }

        // The labels an item calls, directly or from inside a subloop.
        private static IEnumerable<Label> LabelsCalledBy(LoopItem item)
        {
            return item switch
            {
                LabelCallItem call => [call.Label],
                SubloopItem subloop => subloop.UnitItems.SelectMany(LabelsCalledBy),
                _ => [],
            };
        }

        // Each channel's segments, walked as AddmusicK's parser walks the file: the octave carried from one channel into the next, and the q each channel's own.
        private static List<List<Segment>> ParseChannels(IReadOnlyList<(string Header, string Body)> channels, IReadOnlyDictionary<int, IReadOnlySet<int>> barredReleaseNotesByChannel)
        {
            var position = new ParserPosition();
            var segmentsByChannel = new List<List<Segment>>();

            for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
            {
                position.StartChannel(channels[channelIndex].Header);
                segmentsByChannel.Add(ParseChannelBody(WrittenTokens.Split(channels[channelIndex].Body), position, barredReleaseNotesByChannel.GetValueOrDefault(channelIndex)));
            }

            return segmentsByChannel;
        }

        // One channel body's segments, each a run of events: every note, rest or tie and every command but a q, which is left for the writer to state again
        // and whose byte is priced on the note, rest or tie behind it, an edge standing behind a note under a release run's block where the channel loops
        // inside its release runs and the note does not start at a tick barred from it.
        private static List<Segment> ParseChannelBody(List<string> tokens, ParserPosition position, IReadOnlySet<int>? barredReleaseNoteTicks)
        {
            var segments = new List<Segment> { new() };
            var commandGroup = new List<PhraseEvent>();
            bool isBehindHeldNote = false;
            bool isBehindReleaseNote = false;
            int tick = 0;
            int noteStartTick = 0;
            int durationTicksInForce = MmlDurationEncoding.NoDurationTicks;
            int pendingQByteBytes = 0;

            for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
            {
                string token = tokens[tokenIndex];
                var segment = segments[^1];

                if (token == WrittenTokens.LoopMarker)
                {
                    EndCommandGroup(segment, commandGroup, !isBehindHeldNote);
                    segments.Add(new Segment());
                    durationTicksInForce = MmlDurationEncoding.NoDurationTicks;
                    continue;
                }

                if (token == WrittenTokens.BendCommand && commandGroup.Count == 0 && segment.Events.Count > 0)
                {
                    tokenIndex += AttachBend(segment.Events[^1], tokens, tokenIndex) - 1;
                    continue;
                }

                if (WrittenTokens.IsQ(token))
                {
                    position.Follow(token);
                    pendingQByteBytes = WrittenTokens.CompiledBytesOf(token);
                    continue;
                }

                if (WrittenTokens.TicksOf(token) is null)
                {
                    AddToCommandGroup(commandGroup, token, tokenIndex > 0 && IsRawHex(tokens[tokenIndex - 1]), position);
                    continue;
                }

                bool canGroupHaveEdge = (!isBehindHeldNote || (isBehindReleaseNote && WrittenTokens.IsNote(token))) && !WrittenTokens.IsTie(token);
                var (phraseEvent, lastTokenIndex) = ReadEvent(tokens, tokenIndex, position, ref durationTicksInForce);

                phraseEvent.CanHaveEdgeBefore = EndCommandGroup(segment, commandGroup, canGroupHaveEdge);
                phraseEvent.EstimatedBytes += pendingQByteBytes;
                pendingQByteBytes = 0;
                segment.Events.Add(phraseEvent);
                isBehindHeldNote = position.KeyOffArm is not null && !WrittenTokens.IsRest(token);
                noteStartTick = WrittenTokens.IsNote(token) ? tick : noteStartTick;
                isBehindReleaseNote = barredReleaseNoteTicks is not null && position.IsReleaseArmed && !WrittenTokens.IsRest(token) && !barredReleaseNoteTicks.Contains(noteStartTick);
                tick += phraseEvent.Tokens.Sum(eventToken => WrittenTokens.TicksOf(eventToken) ?? 0);
                tokenIndex = lastTokenIndex;
            }

            EndCommandGroup(segments[^1], commandGroup, !isBehindHeldNote);
            foreach (var segment in segments)
            {
                segment.Items = [.. segment.Events.Select(phraseEvent => (LoopItem)new EventItem(phraseEvent))];
            }

            return segments;
        }

        // Puts a bend and its arguments behind the event in front of it, where the read-ahead finds it, and answers how many tokens it took.
        private static int AttachBend(PhraseEvent phraseEvent, List<string> tokens, int bendIndex)
        {
            var bendTokens = tokens.Skip(bendIndex).Take(WrittenTokens.BendTokenCount).ToList();

            foreach (string bendToken in bendTokens)
            {
                AppendCommandToken(phraseEvent, bendToken);
            }

            return bendTokens.Count;
        }

        // Adds a command to the group in front of the next note, rest or tie as an event of its own, or to the last one where it is a raw hex byte straight behind another.
        private static void AddToCommandGroup(List<PhraseEvent> commandGroup, string token, bool isAfterRawHex, ParserPosition position)
        {
            if (IsRawHex(token) && isAfterRawHex && commandGroup.Count > 0)
            {
                AppendCommandToken(commandGroup[^1], token);
                position.Follow(token);
                return;
            }

            var commandEvent = position.StartEvent();
            commandEvent.IsCommand = true;

            AppendCommandToken(commandEvent, token);
            position.Follow(token);
            commandGroup.Add(commandEvent);
        }

        // Adds a command's token to an event, its key and its bytes.
        private static void AppendCommandToken(PhraseEvent phraseEvent, string token)
        {
            phraseEvent.Tokens.Add(token);
            phraseEvent.PitchlessKey += token + " ";
            phraseEvent.EstimatedBytes += WrittenTokens.CompiledBytesOf(token);
        }

        // Moves the commands gathered in front of a note, rest or tie into the segment, an edge standing among them only where one may at their start and
        // never behind a percussion instrument's @, and answers whether one may stand behind them.
        private static bool EndCommandGroup(Segment segment, List<PhraseEvent> commandGroup, bool canGroupHaveEdge)
        {
            bool canHaveEdge = canGroupHaveEdge;
            foreach (var commandEvent in commandGroup)
            {
                commandEvent.CanHaveEdgeBefore = canHaveEdge;
                canHaveEdge = canHaveEdge && !WrittenTokens.IsPercussionInstrument(commandEvent.Tokens[0]);
            }

            segment.Events.AddRange(commandGroup);
            commandGroup.Clear();
            return canHaveEdge;
        }

        // Whether a token is one raw hex byte, which only belongs with the bytes around it.
        private static bool IsRawHex(string token)
        {
            return token[0] == '$';
        }

        // One note, rest or tie with every piece AddmusicK merges into it, and the index of its last piece.
        private static (PhraseEvent Event, int LastTokenIndex) ReadEvent(List<string> tokens, int firstPieceIndex, ParserPosition position, ref int durationTicksInForce)
        {
            string firstPiece = tokens[firstPieceIndex];
            bool isRest = WrittenTokens.IsRest(firstPiece);
            var phraseEvent = position.StartEvent();
            var pitchlessKey = new StringBuilder(phraseEvent.PitchlessKey);

            int runTicks = 0;
            int lastPieceIndex = firstPieceIndex;
            for (int pieceIndex = firstPieceIndex; pieceIndex < tokens.Count; pieceIndex++)
            {
                string piece = tokens[pieceIndex];
                bool doesMerge = pieceIndex == firstPieceIndex || WrittenTokens.IsTie(piece) || (isRest && WrittenTokens.IsRest(piece));
                if (!doesMerge || WrittenTokens.TicksOf(piece) is not int pieceTicks)
                {
                    break;
                }

                phraseEvent.Tokens.Add(piece);
                pitchlessKey.Append(pieceIndex == firstPieceIndex ? PitchlessPiece(piece) : piece).Append(' ');
                runTicks += pieceTicks;
                lastPieceIndex = pieceIndex;
            }

            phraseEvent.EstimatedBytes = MmlDurationEncoding.EstimatedRunBytes(runTicks, ref durationTicksInForce);
            phraseEvent.PitchlessKey = pitchlessKey.ToString();
            phraseEvent.NoteValue = WrittenTokens.IsNote(firstPiece) ? NoteValueOf(firstPiece, position.Octave) : null;

            return (phraseEvent, lastPieceIndex);
        }

        // A note written as its length alone, its pitch being the event's note value, and any other token as it is.
        private static string PitchlessPiece(string token)
        {
            return WrittenTokens.IsNote(token) ? NotePieceKeyMark + token[WrittenTokens.NoteLengthStartOf(token)..] : token;
        }

        // The note value a note compiles to under the octave.
        private static int NoteValueOf(string noteToken, int octave)
        {
            return SnesPitch.NoteValueFor(octave, SnesPitch.SemitoneForLetter(noteToken[0])) + WrittenTokens.AccidentalSemitonesOf(noteToken);
        }

        // Chooses the label loops greedily over every segment at once and writes each segment's copies as calls, or as a subloop where they all stand back to back.
        private static void FindLabels(List<Segment> segments, LabelFinder finder)
        {
            var takenPhrases = finder.TakePhrases();

            ReplaceCopiesWithLoops(segments, finder.Events, takenPhrases);
        }

        // Writes each taken phrase's copies into the segments as calls of one label, a shifted copy's under its transpose, or as a subloop where the copies
        // are all in one place.
        private static void ReplaceCopiesWithLoops(List<Segment> segments, List<PhraseEvent?> events, List<(List<PhraseCopy> Copies, int Length)> takenPhrases)
        {
            var loopsByStart = new Dictionary<int, (Label? Label, int Count, int EventCount, List<LoopItem> UnitItems, CallTranspose? Transpose)>();

            foreach (var (copies, length) in takenPhrases)
            {
                var runs = RunsOfAdjacentCopies(copies, length);

                if (runs.Count == 1)
                {
                    var unitItems = Enumerable.Range(copies[0].Start, length).Select(eventIndex => (LoopItem)new EventItem(events[eventIndex]!)).ToList();

                    loopsByStart[runs[0].Start] = (null, runs[0].Count, length * runs[0].Count, unitItems, null);
                    continue;
                }

                var bodyItems = LabelBodyItems(events, [.. copies.Select(copy => copy.Start)], length);
                var label = new Label { BodyItems = bodyItems };
                foreach (var run in runs)
                {
                    var transpose = run.Shift == 0 ? null : new CallTranspose(run.Shift, events[run.Start]!.TransposeBefore);
                    loopsByStart[run.Start] = (label, run.Count, length * run.Count, bodyItems, transpose);
                }
            }

            int segmentStart = 0;
            foreach (var segment in segments)
            {
                var items = new List<LoopItem>();

                for (int eventIndex = 0; eventIndex < segment.Events.Count;)
                {
                    if (loopsByStart.TryGetValue(segmentStart + eventIndex, out var loop))
                    {
                        items.Add(loop.Label is { } label ? new LabelCallItem(label, loop.Count, loop.Transpose) : new SubloopItem(loop.UnitItems, loop.Count));
                        eventIndex += loop.EventCount;
                    }
                    else
                    {
                        items.Add(new EventItem(segment.Events[eventIndex]));
                        eventIndex++;
                    }
                }

                segment.Items = items;
                segmentStart += segment.Events.Count + 1;
            }
        }

        // A phrase's copies grouped into runs of copies directly following each other under one shift, each run no longer than a loop can count.
        private static List<CopyRun> RunsOfAdjacentCopies(List<PhraseCopy> copies, int length)
        {
            var runs = new List<CopyRun>();

            foreach (var copy in copies)
            {
                if (runs.Count > 0 && runs[^1].End(length) == copy.Start && runs[^1].Shift == copy.Shift && runs[^1].Count < MaxLoopCount)
                {
                    runs[^1].Copies.Add(copy);
                }
                else
                {
                    runs.Add(new CopyRun([copy]));
                }
            }

            return runs;
        }

        // The bytes a label's call costs where it plays its body shifted by that many semitones, the transposes around a shifted one included.
        private static int CallBytesUnder(int shift)
        {
            return SongDataSize.LabelCallBytes + (shift == 0 ? 0 : ShiftedCallTransposeBytes);
        }

        // The bytes a label's call costs where it stands playing its body shifted by that many semitones, the restatements behind it included.
        private static int CallSiteBytes(int shift)
        {
            return CallBytesUnder(shift) + EdgeRestatementBytes;
        }

        // A label's body, each event allowing an edge in front of it only where it does at every place the label is called from.
        private static List<LoopItem> LabelBodyItems(List<PhraseEvent?> events, List<int> starts, int length)
        {
            var bodyItems = new List<LoopItem>();

            for (int offset = 0; offset < length; offset++)
            {
                var phraseEvent = events[starts[0] + offset]!;
                bool canHaveEdge = starts.All(start => events[start + offset]!.CanHaveEdgeBefore);
                bodyItems.Add(new EventItem(canHaveEdge == phraseEvent.CanHaveEdgeBefore ? phraseEvent : phraseEvent.WithEdgeAllowed(canHaveEdge)));
            }

            return bodyItems;
        }

        // The suffixes of a sequence of ids in order, by prefix doubling: each round sorts on the rank of a suffix's first half and of its second.
        private static int[] SuffixArray(List<int> ids)
        {
            int count = ids.Count;
            var suffixArray = new int[count];
            var rank = new int[count];
            var nextRank = new int[count];
            var sortKeys = new long[count];

            for (int index = 0; index < count; index++)
            {
                suffixArray[index] = index;
                rank[index] = ids[index];
            }

            for (int halfLength = 1; ; halfLength <<= 1)
            {
                for (int index = 0; index < count; index++)
                {
                    int suffix = suffixArray[index];
                    sortKeys[index] = ((long)rank[suffix] << SortKeyRankShift) | (uint)(suffix + halfLength < count ? rank[suffix + halfLength] + 1 : 0);
                }

                Array.Sort(sortKeys, suffixArray);

                nextRank[suffixArray[0]] = 0;
                for (int index = 1; index < count; index++)
                {
                    nextRank[suffixArray[index]] = nextRank[suffixArray[index - 1]] + (sortKeys[index] != sortKeys[index - 1] ? 1 : 0);
                }

                Array.Copy(nextRank, rank, count);
                if (rank[suffixArray[count - 1]] == count - 1 || halfLength >= count)
                {
                    break;
                }
            }

            return suffixArray;
        }

        // Kasai's longest common prefix of each suffix with the one before it in the suffix array, none for the first.
        private static int[] LongestCommonPrefixes(List<int> ids, int[] suffixArray)
        {
            int count = ids.Count;
            var rank = new int[count];
            for (int index = 0; index < count; index++)
            {
                rank[suffixArray[index]] = index;
            }

            var longestCommonPrefixes = new int[count];
            int prefixLength = 0;

            for (int suffix = 0; suffix < count; suffix++)
            {
                if (rank[suffix] == 0)
                {
                    prefixLength = 0;
                    continue;
                }

                int suffixBefore = suffixArray[rank[suffix] - 1];
                while (suffix + prefixLength < count && suffixBefore + prefixLength < count && ids[suffix + prefixLength] == ids[suffixBefore + prefixLength])
                {
                    prefixLength++;
                }

                longestCommonPrefixes[rank[suffix]] = prefixLength;
                if (prefixLength > 0)
                {
                    prefixLength--;
                }
            }

            return longestCommonPrefixes;
        }

        // Every interval of the suffix array whose suffixes share a prefix longer than they share with anything outside it: the prefix's length and the interval's two ends.
        private static IEnumerable<(int Length, int FirstRank, int LastRank)> SharedPrefixIntervals(int[] longestCommonPrefixes)
        {
            var openIntervals = new Stack<(int Length, int FirstRank)>();
            openIntervals.Push((0, 0));

            for (int rank = 1; rank <= longestCommonPrefixes.Length; rank++)
            {
                int prefixLength = rank < longestCommonPrefixes.Length ? longestCommonPrefixes[rank] : 0;
                int firstRank = rank - 1;

                while (prefixLength < openIntervals.Peek().Length)
                {
                    var closing = openIntervals.Pop();
                    yield return (closing.Length, closing.FirstRank, rank - 1);
                    firstRank = closing.FirstRank;
                }

                if (prefixLength > openIntervals.Peek().Length)
                {
                    openIntervals.Push((prefixLength, firstRank));
                }
            }
        }

        // A list with its repeats directly following each other taken into subloops, the one saving the most bytes first and again until none saves any.
        private static List<LoopItem> WithSubloops(List<LoopItem> items)
        {
            return WithRepeatsTaken(items, AsSubloop);
        }

        // A list with its repeats directly following each other taken into loops of one kind, the one saving the most bytes first and again until none saves any.
        private static List<LoopItem> WithRepeatsTaken(List<LoopItem> items, RepeatLoopKind kind)
        {
            var idsByKey = new Dictionary<object, int>();

            while (true)
            {
                if (BestRepeat(items, idsByKey, kind) is not { } bestRepeat)
                {
                    return items;
                }

                var loop = kind.LoopOf(items.GetRange(bestRepeat.Start, bestRepeat.UnitLength), bestRepeat.Count);
                items = [.. items.Take(bestRepeat.Start), loop, .. items.Skip(bestRepeat.Start + bestRepeat.UnitLength * bestRepeat.Count)];
            }
        }

        // The repeat saving the most bytes in a list written as a loop of one kind, or null where none saves any.
        private static (int Start, int UnitLength, int Count)? BestRepeat(List<LoopItem> items, Dictionary<object, int> idsByKey, RepeatLoopKind kind)
        {
            int itemCount = items.Count;
            var ids = new int[itemCount];
            var bytesBefore = new int[itemCount + 1];
            var canHaveEdgeAt = new bool[itemCount + 1];

            for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                ids[itemIndex] = kind.RepeatKeyOf(items[itemIndex]) is { } key ? IdFor(idsByKey, key) : NoId - itemIndex;
                bytesBefore[itemIndex + 1] = bytesBefore[itemIndex] + ItemBytes(items[itemIndex]);
                canHaveEdgeAt[itemIndex] = items[itemIndex] is not EventItem { Event.CanHaveEdgeBefore: false };
            }

            canHaveEdgeAt[itemCount] = true;

            (int Start, int UnitLength, int Count, int BytesSaved) best = default;
            var matchingRun = new int[itemCount + 1];

            for (int unitLength = 1; unitLength <= Math.Min(MaxSubloopUnitEvents, itemCount / MinSubloopCount); unitLength++)
            {
                matchingRun[itemCount - unitLength] = 0;
                for (int itemIndex = itemCount - unitLength - 1; itemIndex >= 0; itemIndex--)
                {
                    matchingRun[itemIndex] = ids[itemIndex] >= 0 && ids[itemIndex] == ids[itemIndex + unitLength] ? matchingRun[itemIndex + 1] + 1 : 0;
                }

                for (int start = 0; start + MinSubloopCount * unitLength <= itemCount; start++)
                {
                    if (matchingRun[start] < unitLength || !canHaveEdgeAt[start])
                    {
                        continue;
                    }

                    int copyCount = CopiesEndingOnEdges(canHaveEdgeAt, start, unitLength, 1 + matchingRun[start] / unitLength);
                    if (copyCount < MinSubloopCount)
                    {
                        continue;
                    }

                    int unitBytes = bytesBefore[start + unitLength] - bytesBefore[start];
                    int bytesSaved = bytesBefore[start + copyCount * unitLength] - bytesBefore[start] - kind.LoopBytesOver(unitBytes);

                    if (bytesSaved > best.BytesSaved)
                    {
                        best = (start, unitLength, copyCount, bytesSaved);
                    }
                }
            }

            return best.BytesSaved > 0 ? (best.Start, best.UnitLength, best.Count) : null;
        }

        // What an item is matched by in a subloop's repeats, null where no subloop can hold it.
        private static object? SubloopKeyOf(LoopItem item)
        {
            return item switch
            {
                LabelCallItem call when !call.Label.HasSubloop => (call.Label, call.Count, call.Transpose),
                _ => LabelBodyKeyOf(item),
            };
        }

        // The bytes items are estimated to compile to where they stand, a called label's body not counted.
        private static int ItemsBytes(List<LoopItem> items)
        {
            return items.Sum(ItemBytes);
        }

        // The bytes an item is estimated to compile to where it stands, a called label's body not counted.
        private static int ItemBytes(LoopItem item)
        {
            return item switch
            {
                EventItem eventItem => eventItem.Event.EstimatedBytes,
                LabelCallItem call => CallSiteBytes(call.Transpose?.Shift ?? NoShift),
                SubloopItem subloop => SubloopBytesOver(ItemsBytes(subloop.UnitItems)),
                _ => 0,
            };
        }

        // The id a key is matched by, the next one unused where the key is new.
        private static int IdFor<TKey>(Dictionary<TKey, int> idsByKey, TKey key) where TKey : notnull
        {
            if (!idsByKey.TryGetValue(key, out int id))
            {
                idsByKey[key] = id = idsByKey.Count;
            }

            return id;
        }

        // How many of the matching copies a subloop can take: each starting where an edge may stand and the last ending at one.
        private static int CopiesEndingOnEdges(bool[] canHaveEdgeAt, int start, int unitLength, int matchingCopies)
        {
            int copyCount = 1;
            while (copyCount < Math.Min(matchingCopies, MaxLoopCount) && canHaveEdgeAt[start + copyCount * unitLength])
            {
                copyCount++;
            }

            while (copyCount > 1 && !canHaveEdgeAt[start + copyCount * unitLength])
            {
                copyCount--;
            }

            return copyCount;
        }

        // Every long note, rest or tie standing outside a subloop written with a subloop of its one-byte pieces, where that saves bytes: in each segment,
        // and in the body of each label no subloop calls.
        private static void WriteLongRunsAsLoops(List<Segment> segments)
        {
            var labelsCalledInsideSubloops = segments.SelectMany(segment => segment.Items.OfType<SubloopItem>()).SelectMany(LabelsCalledBy).ToHashSet();
            foreach (var label in segments.SelectMany(LabelsCalledIn).Distinct().Where(label => !labelsCalledInsideSubloops.Contains(label)).ToList())
            {
                label.BodyItems = WithLongRunsAsLoops(label.BodyItems);
            }

            foreach (var segment in segments)
            {
                segment.Items = WithLongRunsAsLoops(segment.Items);
            }
        }

        // A list with each long note, rest or tie standing in it written with a subloop of its one-byte pieces, where that saves bytes.
        private static List<LoopItem> WithLongRunsAsLoops(List<LoopItem> items)
        {
            var writtenItems = new List<LoopItem>();

            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                if (CanRunBeCutAt(items, itemIndex) && RunLoopItems(((EventItem)items[itemIndex]).Event) is { } runLoopItems)
                {
                    writtenItems.AddRange(runLoopItems);
                    continue;
                }

                writtenItems.Add(items[itemIndex]);
            }

            return writtenItems;
        }

        // Whether the item at an index is a note, rest or tie that may be written in pieces: one holding nothing but its pieces, a rest where a loop edge
        // may stand on both sides of it and no key-off block is armed over it unless a rest stands before it, and a note or tie where neither a key-off
        // block nor an arpeggio runs over it.
        private static bool CanRunBeCutAt(List<LoopItem> items, int itemIndex)
        {
            if (items[itemIndex] is not EventItem { Event: { IsRun: true } run } || run.Tokens.Any(token => WrittenTokens.TicksOf(token) is null))
            {
                return false;
            }

            if (!run.IsRest)
            {
                return !run.IsUnderKeyOffBlock && !run.IsUnderArpeggio;
            }

            bool canHaveEdgeBehind = itemIndex + 1 == items.Count || items[itemIndex + 1] is not EventItem { Event.CanHaveEdgeBefore: false };
            return run.CanHaveEdgeBefore && canHaveEdgeBehind && (!run.IsUnderKeyOffBlock || IsRestBefore(items, itemIndex));
        }

        // Whether the last note, rest or tie before the item at an index in the same list is a rest, nothing but commands standing between them.
        private static bool IsRestBefore(List<LoopItem> items, int itemIndex)
        {
            for (int index = itemIndex - 1; index >= 0; index--)
            {
                if (items[index] is not EventItem { Event: var phraseEvent })
                {
                    return false;
                }

                if (phraseEvent.IsRun)
                {
                    return phraseEvent.IsRest;
                }
            }

            return false;
        }

        // A note, rest or tie written with a subloop of pieces of the length AddmusicK cuts a long one into and what is left over behind it, or null
        // where that saves no bytes. A note or tie keeps its first piece in front of the subloop and its last behind it, every piece where AddmusicK
        // cuts it.
        private static List<LoopItem>? RunLoopItems(PhraseEvent run)
        {
            const int PieceTicks = MmlDurationEncoding.TicksPerAutoTiedPiece;

            int runTicks = run.Tokens.Sum(token => WrittenTokens.TicksOf(token) ?? 0);
            if (runTicks <= MmlDurationEncoding.MaxTicksWithoutAutoTies)
            {
                return null;
            }

            int lastPieceTicks = MmlDurationEncoding.LastTiedPieceTicks(runTicks);
            int firstPieceTicks = run.IsRest ? 0 : PieceTicks;
            int loopableTicks = run.IsRest ? runTicks : runTicks - lastPieceTicks;
            int pieceCount = Math.Min((loopableTicks - firstPieceTicks) / PieceTicks, MaxLoopCount);
            int leftoverTicks = runTicks - firstPieceTicks - pieceCount * PieceTicks;

            if (!run.IsRest && leftoverTicks != lastPieceTicks && leftoverTicks <= MmlDurationEncoding.MaxTicksWithoutAutoTies)
            {
                pieceCount--;
                leftoverTicks += PieceTicks;
            }

            if (pieceCount < MinSubloopCount)
            {
                return null;
            }

            var loopItems = new List<LoopItem>();
            int loopBytes = 0;

            if (firstPieceTicks > 0)
            {
                var firstPiece = run.RunPiece(WrittenTokens.RunOfLength(WrittenTokens.RunSpellingOf(run.Tokens[0]), firstPieceTicks), MmlDurationEncoding.NoDurationTicks);
                loopItems.Add(new EventItem(firstPiece));
                loopBytes += firstPiece.EstimatedBytes;
            }

            var piece = run.RunPiece(FollowingPieceOf(run, PieceTicks), MmlDurationEncoding.NoDurationTicks);
            loopItems.Add(new SubloopItem([new EventItem(piece)], pieceCount));
            loopBytes += SubloopBytesOver(piece.EstimatedBytes);

            if (leftoverTicks > 0)
            {
                var leftover = run.RunPiece(FollowingPieceOf(run, leftoverTicks), PieceTicks);
                loopItems.Add(new EventItem(leftover));
                loopBytes += leftover.EstimatedBytes;
            }

            return loopBytes < run.EstimatedBytes ? loopItems : null;
        }

        // A piece of a note, rest or tie past its first, that many ticks long: a rest's a rest, a note's or tie's a tie.
        private static string FollowingPieceOf(PhraseEvent run, int ticks)
        {
            return run.IsRest ? WrittenTokens.Rest(ticks) : WrittenTokens.Tie(ticks);
        }

        // The tokens that part two rests written side by side: the octave the parser holds, stated.
        public static IEnumerable<string> RestPartingTokens(int octave)
        {
            return StatedOctaveTokens(octave);
        }

        // An octave stated outright, held to the ones AddmusicK takes, with the marks reaching past them.
        private static IEnumerable<string> StatedOctaveTokens(int octave)
        {
            int statedOctave = Math.Clamp(octave, LowestStatedOctave, HighestStatedOctave);
            yield return LetteredText('o', statedOctave);

            for (int step = statedOctave; step < octave; step++)
            {
                yield return WrittenTokens.OctaveUp;
            }

            for (int step = statedOctave; step > octave; step--)
            {
                yield return WrittenTokens.OctaveDown;
            }
        }

        // Every channel's body written back out with its loops, and how many labels were numbered.
        private static (List<string> Bodies, int LabelCount) WriteChannels(IReadOnlyList<(string Header, string Body)> channels, List<List<Segment>> segmentsByChannel)
        {
            var writer = new LoopWriter();
            var bodies = new List<string>();

            for (int channelIndex = 0; channelIndex < segmentsByChannel.Count; channelIndex++)
            {
                var segments = segmentsByChannel[channelIndex];
                writer.StartChannel(channels[channelIndex].Header);

                var output = new List<string>();
                for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                {
                    if (segmentIndex > 0)
                    {
                        writer.WriteLoopMarker(output);
                    }

                    writer.WriteItems(segments[segmentIndex].Items, output);
                }

                bodies.Add(string.Join(' ', output));
            }

            return (bodies, writer.LabelCount);
        }

        // One label's body written as a replacement, and the channels it is defined or called in.
        public sealed record Phrase(int LabelNumber, string Text, IReadOnlySet<int> ChannelIndexes)
        {
            // The replacement's name, which nothing else the export writes starts with.
            public string Name
            {
                get
                {
                    return NameFor(LabelNumber);
                }
            }

            // Whether more than one channel defines or calls it.
            public bool IsShared
            {
                get
                {
                    return ChannelIndexes.Count > 1;
                }
            }

            // The replacement's name for a label's number.
            public static string NameFor(int labelNumber)
            {
                return $"P{labelNumber}";
            }
        }

        // One note, rest or tie as AddmusicK's parser reads it, with the bends behind it, or one command.
        private sealed class PhraseEvent
        {
            public readonly List<string> Tokens = [];
            public string PitchlessKey = "";
            public int? NoteValue;
            public int EstimatedBytes;
            public bool IsCommand;
            public bool CanHaveEdgeBefore;
            public bool IsUnderKeyOffBlock;
            public bool IsUnderArpeggio;
            public int OctaveBefore;
            public int QBefore;
            public int TransposeBefore;

            // The part of its key the parser's state writes, which every key starts with.
            public string StateKey = "";

            // What it compiles to, pitch included, which every copy of a phrase has to match.
            public string CompiledKey
            {
                get
                {
                    return NoteValue is int noteValue ? PitchlessKey + NoteValueKeyMark + noteValue : PitchlessKey;
                }
            }

            // Whether it is an $FA $02 transpose.
            public bool IsTransposeCommand
            {
                get
                {
                    return IsCommand && TransposeName.IsNameOf(Tokens[0]);
                }
            }

            // Whether it plays the same in a copy called under a transpose: neither a transpose nor a percussion instrument's @.
            public bool CanPlayUnderShift
            {
                get
                {
                    return !IsTransposeCommand && !(IsCommand && WrittenTokens.IsPercussionInstrument(Tokens[0]));
                }
            }

            // Whether it is a note, rest or tie.
            public bool IsRun
            {
                get
                {
                    return !IsCommand;
                }
            }

            // Whether it is a note, which keys the voice on.
            public bool IsNote
            {
                get
                {
                    return IsRun && WrittenTokens.IsNote(Tokens[0]);
                }
            }

            // Whether it is a rest.
            public bool IsRest
            {
                get
                {
                    return IsRun && WrittenTokens.IsRest(Tokens[0]);
                }
            }

            // Whether it states or moves the octave.
            public bool IsOctaveMark
            {
                get
                {
                    return IsCommand && WrittenTokens.IsOctaveMark(Tokens[0]);
                }
            }

            // Whether what it does depends on the octave in front of it: a note, or a mark moving the octave from where it is.
            public bool DoesReadOctave
            {
                get
                {
                    return IsNote || (IsCommand && WrittenTokens.IsOctaveShift(Tokens[0]));
                }
            }

            // The same event where an edge may or may not stand in front of it.
            public PhraseEvent WithEdgeAllowed(bool canHaveEdge)
            {
                var copy = StateCopy(canHaveEdge);
                copy.PitchlessKey = PitchlessKey;
                copy.NoteValue = NoteValue;
                copy.EstimatedBytes = EstimatedBytes;
                copy.Tokens.AddRange(Tokens);
                return copy;
            }

            // The same event as an object of its own, so writing one place it stands never marks another as written.
            public PhraseEvent Copy()
            {
                return WithEdgeAllowed(CanHaveEdgeBefore);
            }

            // A note, rest or tie piece as written, read under the same state as this event at this event's note value, an edge allowed in front of it,
            // priced under the duration in force.
            public PhraseEvent RunPiece(string pieceToken, int durationTicksInForce)
            {
                var piece = StateCopy(true);
                piece.Tokens.Add(pieceToken);
                piece.PitchlessKey += PitchlessPiece(pieceToken) + " ";
                piece.NoteValue = WrittenTokens.IsNote(pieceToken) ? NoteValue : null;
                piece.EstimatedBytes = MmlDurationEncoding.EstimatedRunBytes(WrittenTokens.TicksOf(pieceToken) ?? 0, ref durationTicksInForce);
                return piece;
            }

            // An event read under the same state as this one, holding no tokens yet, an edge allowed in front of it or not.
            private PhraseEvent StateCopy(bool canHaveEdge)
            {
                return new PhraseEvent
                {
                    StateKey = StateKey,
                    PitchlessKey = StateKey,
                    IsCommand = IsCommand,
                    CanHaveEdgeBefore = canHaveEdge,
                    IsUnderKeyOffBlock = IsUnderKeyOffBlock,
                    IsUnderArpeggio = IsUnderArpeggio,
                    OctaveBefore = OctaveBefore,
                    QBefore = QBefore,
                    TransposeBefore = TransposeBefore,
                };
            }
        }

        // A stretch of a channel no loop may cross: the part before the loop marker or after it.
        private sealed class Segment
        {
            public readonly List<PhraseEvent> Events = [];
            public List<LoopItem> Items = [];
        }

        // One thing a segment or a loop body is written as: an event, a label's call or a subloop.
        private abstract record LoopItem;

        // An event written as it is.
        private sealed record EventItem(PhraseEvent Event) : LoopItem;

        // A label played that many times, under a transpose where the copies it stands for play its body shifted.
        private sealed record LabelCallItem(Label Label, int Count, CallTranspose? Transpose) : LoopItem;

        // The semitones a call shifts its label's body by, and the transpose in force where it stands, which is put back behind it.
        private sealed record CallTranspose(int Shift, int TransposeAfter)
        {
            // The transpose the body plays under.
            public int TransposeDuring
            {
                get
                {
                    return TransposeAfter + Shift;
                }
            }
        }

        // One place a phrase is copied to, and the semitones it plays shifted from the first copy.
        private readonly record struct PhraseCopy(int Start, int Shift);

        // Copies of a phrase directly following each other under one shift, written as one call.
        private sealed record CopyRun(List<PhraseCopy> Copies)
        {
            // Where the first copy starts.
            public int Start
            {
                get
                {
                    return Copies[0].Start;
                }
            }

            // How many copies it holds.
            public int Count
            {
                get
                {
                    return Copies.Count;
                }
            }

            // The semitones every copy plays shifted from the phrase's first copy.
            public int Shift
            {
                get
                {
                    return Copies[0].Shift;
                }
            }

            // Where the last copy ends.
            public int End(int length)
            {
                return Start + Count * length;
            }
        }

        // A subloop playing its unit that many times.
        private sealed record SubloopItem(List<LoopItem> UnitItems, int Count) : LoopItem;

        // One way of writing repeats directly following each other as one loop: what an item is matched by, null where the loop cannot hold it, what
        // copies of a unit cost written as the loop, and the loop playing a unit that many times.
        private sealed record RepeatLoopKind(Func<LoopItem, object?> RepeatKeyOf, Func<int, int> LoopBytesOver, Func<List<LoopItem>, int, LoopItem> LoopOf);

        // A label loop's body and the number it is written under, none until it is first written.
        private sealed class Label
        {
            public List<LoopItem> BodyItems = [];
            public int Number;

            // Whether its body holds a subloop.
            public bool HasSubloop
            {
                get
                {
                    return BodyItems.Any(item => item is SubloopItem);
                }
            }
        }

        // The octave, the q, the key-off block armed, the legato's bit, what the key-on slot holds between runs, the arpeggio in force, and whether a q byte
        // is due, as AddmusicK's parser and the driver hold them at a point of the text.
        private sealed class ParserPosition
        {
            public int Octave = SnesPitch.MmlStartOctave;
            public int Q;
            public int Transpose;
            public bool IsLegatoBitSet;
            public string? ArpeggioCommandInForce;

            // Whether the driver may hold another q than the parser: from the loop marker until a q is written.
            public bool IsQByteDue;

            // The arm of the key-off block in the ordinary slot - a held note's or a release run's - or null where none is armed.
            public string? KeyOffArm;

            // The arm of what the key-on slot holds between the key-on blocks' runs - the legato's block, the envelope reset or both - or null where it
            // holds neither.
            public string? KeyOnSlotArm;

            // Whether a release run's key-off block is armed.
            public bool IsReleaseArmed
            {
                get
                {
                    return KeyOffArm is { } arm && ReleaseRuns.IsArmPlaceholder(arm);
                }
            }

            // Starts a channel on its own q with no transpose, no key-off block armed, the legato off and no arpeggio running, the octave carrying over
            // from the channel before, and follows its header.
            public void StartChannel(string header)
            {
                Q = WrittenTokens.ChannelStartQ;
                Transpose = 0;
                KeyOffArm = null;
                IsLegatoBitSet = false;
                KeyOnSlotArm = null;
                ArpeggioCommandInForce = null;
                IsQByteDue = false;

                foreach (string token in WrittenTokens.Split(header))
                {
                    Follow(token);
                }
            }

            // What a token does to the octave, the q and whether a q byte is due, the transpose, the key-off block's arm, the legato's bit, what the key-on slot holds between runs, and the arpeggio.
            public void Follow(string token)
            {
                if (WrittenTokens.IsOctave(token))
                {
                    Octave = token[1] - '0';
                }
                else if (TransposeName.IsNameOf(token))
                {
                    Transpose = TransposeOf(token);
                }
                else if (token == WrittenTokens.OctaveDown)
                {
                    Octave--;
                }
                else if (token == WrittenTokens.OctaveUp)
                {
                    Octave++;
                }
                else if (WrittenTokens.IsQ(token))
                {
                    Q = Convert.ToInt32(token[1..], HexRadix);
                    IsQByteDue = false;
                }
                else if (IsKeyOffArmCommand(token))
                {
                    KeyOffArm = token;
                }
                else if (HoldOffName.IsNameOf(token))
                {
                    KeyOffArm = null;
                    KeyOnSlotArm = null;
                }
                else if (LegatoName.IsNameOf(token))
                {
                    IsLegatoBitSet = !IsLegatoBitSet;
                }
                else if (KeyOnSlotBlockCommandsOf(token) is not null)
                {
                    KeyOnSlotArm = token;
                }
                else if (token == KeyOnClearCommand)
                {
                    KeyOnSlotArm = null;
                }
                else if (ArpName.IsNameOf(token) || ArpOffName.IsNameOf(token))
                {
                    ArpeggioCommandInForce = ArpName.IsNameOf(token) ? token : null;
                }
            }

            // A new event under the state here, its key opening on the q in force, the key-off block armed, the legato's bit, what the key-on slot holds between runs, and
            // the arpeggio in force, which every copy of a phrase has to agree on.
            public PhraseEvent StartEvent()
            {
                string legatoBitMark = IsLegatoBitSet ? LegatoBitSetKeyMark : "";
                string stateKey = $"{Q:X2}{KeyOffArm}{legatoBitMark}{KeyOnSlotArm}{ArpeggioCommandInForce}|";

                return new PhraseEvent
                {
                    OctaveBefore = Octave,
                    QBefore = Q,
                    TransposeBefore = Transpose,
                    IsUnderKeyOffBlock = KeyOffArm is not null,
                    IsUnderArpeggio = ArpeggioCommandInForce is not null,
                    StateKey = stateKey,
                    PitchlessKey = stateKey,
                };
            }
        }

        // The text written out, holding the octave and the q the parser will be under at each point of it, and numbering the labels as it defines them.
        private sealed class LoopWriter
        {
            private readonly ParserPosition _position = new();
            private readonly HashSet<PhraseEvent> _eventsWrittenOutsideTheirLoop = [];

            public int LabelCount { get; private set; }

            // Writes the loop marker, behind which the driver may hold another q than the parser until one is written.
            public void WriteLoopMarker(List<string> output)
            {
                output.Add(WrittenTokens.LoopMarker);
                _position.IsQByteDue = true;
            }

            // Starts a channel on its own q, following its header, which is written as it was.
            public void StartChannel(string header)
            {
                _position.StartChannel(header);
            }

            // Writes items with the octave and the q each event expects restated in front of it where the parser holds another, a transpose left out where
            // the next item sets another before any note.
            public void WriteItems(List<LoopItem> items, List<string> output)
            {
                for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
                {
                    var nextItem = itemIndex + 1 < items.Count ? items[itemIndex + 1] : null;

                    switch (items[itemIndex])
                    {
                        case EventItem { Event.IsTransposeCommand: true } when SetsTransposeFirst(nextItem):
                            break;

                        case EventItem { Event: var phraseEvent } when !_eventsWrittenOutsideTheirLoop.Contains(phraseEvent):
                            WriteEvent(phraseEvent, output);
                            break;

                        case LabelCallItem { Transpose: { } transpose } call:
                            WriteShiftedLabelCall(call, transpose, nextItem, output);
                            break;

                        case LabelCallItem { Label: var label, Count: var count }:
                            WriteLabelCall(label, count, output);
                            break;

                        case SubloopItem { UnitItems: var unitItems, Count: var count }:
                            WriteLoop(unitItems, WrittenTokens.SubloopOpen, WrittenTokens.SubloopClose + WrittenCount(count), output);
                            break;
                    }
                }
            }

            // Whether an item sets the transpose before it plays any note: a transpose, or a call under one.
            private static bool SetsTransposeFirst(LoopItem? item)
            {
                return item is EventItem { Event.IsTransposeCommand: true } or LabelCallItem { Transpose: not null };
            }

            // Writes a call under the transpose its body plays shifted by, and the transpose in force put back behind it unless the next item sets another
            // before any note.
            private void WriteShiftedLabelCall(LabelCallItem call, CallTranspose transpose, LoopItem? nextItem, List<string> output)
            {
                WriteToken(TransposeToken(transpose.TransposeDuring), output);
                WriteLabelCall(call.Label, call.Count, output);

                if (!SetsTransposeFirst(nextItem))
                {
                    WriteToken(TransposeToken(transpose.TransposeAfter), output);
                }
            }

            // Writes a token and follows what it does to the parser.
            private void WriteToken(string token, List<string> output)
            {
                _position.Follow(token);
                output.Add(token);
            }

            // Writes an event with the octave and the q it reads restated in front of it where the parser holds others, and a rest kept apart from a
            // rest written straight before it.
            private void WriteEvent(PhraseEvent phraseEvent, List<string> output)
            {
                RestateOctaveFor(phraseEvent, output);
                RestateQFor(phraseEvent, output);
                PartFromRestBefore(phraseEvent, output);

                foreach (string token in phraseEvent.Tokens)
                {
                    WriteToken(token, output);
                }
            }

            // Parts a rest written straight behind another from it.
            private void PartFromRestBefore(PhraseEvent phraseEvent, List<string> output)
            {
                if (!phraseEvent.IsRest || output.Count == 0 || !WrittenTokens.IsRest(output[^1]))
                {
                    return;
                }

                foreach (string token in RestPartingTokens(_position.Octave))
                {
                    WriteToken(token, output);
                }
            }

            // States the octave an event was read under in front of it where it reads the octave and the parser holds another.
            private void RestateOctaveFor(PhraseEvent phraseEvent, List<string> output)
            {
                if (!phraseEvent.DoesReadOctave || _position.Octave == phraseEvent.OctaveBefore)
                {
                    return;
                }

                output.AddRange(StatedOctaveTokens(phraseEvent.OctaveBefore));
                _position.Octave = phraseEvent.OctaveBefore;
            }

            // States the q an event was read under in front of it where it is a note, rest or tie and the parser holds another, or where it is a note and a
            // q byte is due.
            private void RestateQFor(PhraseEvent phraseEvent, List<string> output)
            {
                if (!phraseEvent.IsRun || (_position.Q == phraseEvent.QBefore && !(_position.IsQByteDue && phraseEvent.IsNote)))
                {
                    return;
                }

                WriteToken(WrittenTokens.HexLettered('q', phraseEvent.QBefore), output);
            }

            // Writes a loop with the octave marks ahead of its first note and behind its last just outside it, and the octave and the q its first ones are
            // read under stated in front of it.
            private void WriteLoop(List<LoopItem> bodyItems, string opening, string closing, List<string> output)
            {
                var eventsInReadOrder = EventsInReadOrder(bodyItems, []).ToList();
                var leadingMarks = MarksAheadOfReaders(eventsInReadOrder);
                var trailingMarks = MarksAheadOfReaders(Enumerable.Reverse(eventsInReadOrder).Where(phraseEvent => !leadingMarks.Contains(phraseEvent)));

                trailingMarks.Reverse();
                _eventsWrittenOutsideTheirLoop.UnionWith(leadingMarks);
                _eventsWrittenOutsideTheirLoop.UnionWith(trailingMarks);

                foreach (var mark in leadingMarks)
                {
                    WriteEvent(mark, output);
                }

                if (eventsInReadOrder.Find(phraseEvent => phraseEvent.IsNote) is { } firstNote)
                {
                    RestateOctaveFor(firstNote, output);
                }

                if (eventsInReadOrder.Find(phraseEvent => phraseEvent.IsRun) is { } firstRun)
                {
                    RestateQFor(firstRun, output);
                }

                output.Add(opening);
                WriteItems(bodyItems, output);
                output.Add(closing);

                foreach (var mark in trailingMarks)
                {
                    WriteEvent(mark, output);
                }
            }

            // The events a loop's text holds in the order the parser reads them, reaching into the subloops it opens and the labels it defines, a call of
            // a label written already reading nothing, and an event written outside its loop already left out.
            private IEnumerable<PhraseEvent> EventsInReadOrder(List<LoopItem> items, HashSet<Label> labelsDefined)
            {
                foreach (var item in items)
                {
                    if (item is EventItem { Event: var phraseEvent })
                    {
                        if (!_eventsWrittenOutsideTheirLoop.Contains(phraseEvent))
                        {
                            yield return phraseEvent;
                        }

                        continue;
                    }

                    if (ItemsReadInside(item, labelsDefined) is not { } innerItems)
                    {
                        continue;
                    }

                    foreach (var innerEvent in EventsInReadOrder(innerItems, labelsDefined))
                    {
                        yield return innerEvent;
                    }
                }
            }

            // The items whose text a loop item has the parser read: a subloop's unit, or the body of a label defined there, none for any other call.
            private static List<LoopItem>? ItemsReadInside(LoopItem item, HashSet<Label> labelsDefined)
            {
                return item switch
                {
                    SubloopItem subloop => subloop.UnitItems,
                    LabelCallItem { Label: var label } when label.Number == 0 && labelsDefined.Add(label) => label.BodyItems,
                    _ => null,
                };
            }

            // The octave marks ahead of the first note, in the order given.
            private static List<PhraseEvent> MarksAheadOfReaders(IEnumerable<PhraseEvent> events)
            {
                var marks = new List<PhraseEvent>();
                bool hasPassedNote = false;

                foreach (var phraseEvent in events)
                {
                    if (phraseEvent.IsOctaveMark && !hasPassedNote)
                    {
                        marks.Add(phraseEvent);
                        continue;
                    }

                    hasPassedNote |= phraseEvent.IsNote;
                }

                return marks;
            }

            // Writes a label's definition where it is first called, numbering it, and a call of it everywhere else.
            private void WriteLabelCall(Label label, int count, List<string> output)
            {
                if (label.Number != 0)
                {
                    output.Add(WrittenTokens.LabelCall(label.Number) + WrittenCount(count));
                    return;
                }

                label.Number = ++LabelCount;
                WriteLoop(label.BodyItems, WrittenTokens.LabelCall(label.Number) + WrittenTokens.LabelDefinitionOpening, "]" + WrittenCount(count), output);
            }

            // A loop's count as written behind its close, nothing where it plays once.
            private static string WrittenCount(int count)
            {
                return count > 1 ? $"{count}" : "";
            }

        }

        // Finds the label loops greedily over every segment's events end to end, each round taking the repeated phrase that saves the most bytes.
        private sealed class LabelFinder
        {
            private readonly CopySearch _exactSearch;
            private readonly int[] _bytesBefore;
            private readonly bool[] _isClaimed;
            private readonly int[] _claimedBefore;
            private CopySearch? _shiftedSearch;

            // Lays every segment's events end to end, each segment closed by a separator no copy matches, and searches them for copies alike event for
            // event.
            private LabelFinder(List<Segment> segments)
            {
                foreach (var segment in segments)
                {
                    Events.AddRange(segment.Events);
                    Events.Add(null);
                }

                _bytesBefore = new int[Events.Count + 1];
                for (int index = 0; index < Events.Count; index++)
                {
                    _bytesBefore[index + 1] = _bytesBefore[index] + (Events[index]?.EstimatedBytes ?? 0);
                }

                _isClaimed = new bool[Events.Count];
                _claimedBefore = new int[Events.Count + 1];
                _exactSearch = new CopySearch(IdsMatchingExactCopies());
            }

            // Every segment's events end to end, a null where one segment ends.
            public List<PhraseEvent?> Events { get; } = [];

            // A finder over the segments matching copies alike event for event.
            public static LabelFinder MatchingExactCopies(List<Segment> segments)
            {
                return new LabelFinder(segments);
            }

            // A finder over the segments matching copies alike event for event, and copies one plays shifted by whole semitones from another as well,
            // the segments' octave marks taken out for the writer to state again in front of each note that reads another.
            public static LabelFinder MatchingShiftedCopies(List<Segment> segments)
            {
                foreach (var segment in segments)
                {
                    segment.Events.RemoveAll(phraseEvent => phraseEvent.IsOctaveMark);
                }

                var finder = new LabelFinder(segments);
                finder._shiftedSearch = new CopySearch(finder.IdsMatchingShiftedCopies());
                return finder;
            }

            // The phrases taken, each its copies and its length in events, the most saving first.
            public List<(List<PhraseCopy> Copies, int Length)> TakePhrases()
            {
                var takenPhrases = new List<(List<PhraseCopy> Copies, int Length)>();
                var candidates = Candidates();

                while (candidates.TryDequeue(out var candidate, out _))
                {
                    var (copies, bytesSaved) = Price(candidate);
                    if (bytesSaved <= 0)
                    {
                        continue;
                    }

                    var priority = PriorityOf(candidate, bytesSaved);
                    if (candidates.TryPeek(out _, out var bestPriority) && priority.CompareTo(bestPriority) > 0)
                    {
                        candidates.Enqueue(candidate, priority);
                        continue;
                    }

                    takenPhrases.Add((copies, candidate.Length));
                    Claim(copies, candidate.Length);
                }

                return takenPhrases;
            }

            // Each event's id where copies match alike event for event, a separator's an id of its own.
            private List<int> IdsMatchingExactCopies()
            {
                var idsByKey = new Dictionary<string, int>();
                var ids = Events.Select(phraseEvent => phraseEvent is null ? NoId : IdFor(idsByKey, phraseEvent.CompiledKey)).ToList();

                return WithOwnIdsForUnmatched(ids, idsByKey.Count);
            }

            // Each event's id where a copy may play shifted from another: a note matched by its step from the note before it in its segment, and an
            // event a transpose plays wrongly given an id of its own, as a separator is.
            private List<int> IdsMatchingShiftedCopies()
            {
                var idsByKey = new Dictionary<string, int>();
                var ids = new List<int>();
                int? noteValueBefore = null;

                foreach (var phraseEvent in Events)
                {
                    if (phraseEvent is null)
                    {
                        ids.Add(NoId);
                        noteValueBefore = null;
                        continue;
                    }

                    ids.Add(phraseEvent.CanPlayUnderShift ? IdFor(idsByKey, ShiftedCopyKeyOf(phraseEvent, noteValueBefore)) : NoId);
                    noteValueBefore = phraseEvent.NoteValue ?? noteValueBefore;
                }

                return WithOwnIdsForUnmatched(ids, idsByKey.Count);
            }

            // What an event has to match in a copy played shifted: a note its step from the note before it, none for its segment's first, and anything
            // else its compiled key.
            private static string ShiftedCopyKeyOf(PhraseEvent phraseEvent, int? noteValueBefore)
            {
                if (phraseEvent.NoteValue is not int noteValue)
                {
                    return phraseEvent.CompiledKey;
                }

                string step = noteValueBefore is int valueBefore ? $"{noteValue - valueBefore}" : "";
                return phraseEvent.PitchlessKey + IntervalKeyMark + step;
            }

            // The ids with every unmatched one given an id of its own, numbered on from the first one free.
            private static List<int> WithOwnIdsForUnmatched(List<int> ids, int firstFreeId)
            {
                int nextFreeId = firstFreeId;
                return [.. ids.Select(id => id == NoId ? nextFreeId++ : id)];
            }

            // Every repeat the searches find, whole and cut back to where an edge may stand in every copy, queued by the most it could save, a copy played
            // shifted reaching back to the note its first step is counted from.
            private PriorityQueue<Candidate, (int, int)> Candidates()
            {
                var candidates = new PriorityQueue<Candidate, (int, int)>();

                EnqueueRepeats(candidates, _exactSearch, (_, _) => 0);
                if (_shiftedSearch is { } shiftedSearch)
                {
                    EnqueueRepeats(candidates, shiftedSearch, (firstRank, lastRank) => StepsBackToNoteBefore(shiftedSearch, firstRank, lastRank));
                }

                return candidates;
            }

            // Queues every repeat a search finds, whole and cut back to where an edge may stand in every copy, its copies reaching back as far as asked.
            private void EnqueueRepeats(PriorityQueue<Candidate, (int, int)> candidates, CopySearch search, Func<int, int, int> stepsBackOf)
            {
                foreach (var (sharedLength, firstRank, lastRank) in SharedPrefixIntervals(LongestCommonPrefixes(search.Ids, search.SortedSuffixes)))
                {
                    int stepsBack = stepsBackOf(firstRank, lastRank);
                    var wholeRepeat = new Candidate(search, firstRank, lastRank, stepsBack, sharedLength + stepsBack);
                    int firstStart = wholeRepeat.FirstStart;
                    if (Events[firstStart] is not { CanHaveEdgeBefore: true })
                    {
                        continue;
                    }

                    candidates.Enqueue(wholeRepeat, PriorityOf(wholeRepeat, MostBytesSaved(wholeRepeat)));
                    EnqueueCutBack(candidates, wholeRepeat, wholeRepeat.Length - 1);

                    int closestStartsApart = ClosestStartsApart(wholeRepeat);
                    if (closestStartsApart < wholeRepeat.Length - 1)
                    {
                        EnqueueCutBack(candidates, wholeRepeat, closestStartsApart);
                    }
                }
            }

            // Queues a repeat cut back to a length, or further to where an edge may stand behind its first copy, where anything is left of it.
            private void EnqueueCutBack(PriorityQueue<Candidate, (int, int)> candidates, Candidate repeat, int length)
            {
                while (length > 0 && !Events[repeat.FirstStart + length]!.CanHaveEdgeBefore)
                {
                    length--;
                }

                if (length > 0)
                {
                    var cutRepeat = repeat with { Length = length };
                    candidates.Enqueue(cutRepeat, PriorityOf(cutRepeat, MostBytesSaved(cutRepeat)));
                }
            }

            // Where a candidate saving that many bytes stands in the queue: the most bytes saved first, and the longer of two saving alike first.
            private static (int, int) PriorityOf(Candidate candidate, int bytesSaved)
            {
                return (-bytesSaved, -candidate.Length);
            }

            // The fewest events between the starts of two of a repeat's copies.
            private static int ClosestStartsApart(Candidate repeat)
            {
                var starts = repeat.Starts().Order().ToList();
                return starts.Zip(starts.Skip(1), (start, nextStart) => nextStart - start).Min();
            }

            // How far back every copy a shifted search matched reaches alike: over the commands in front of it and the note before its first step, which
            // that step is counted from.
            private int StepsBackToNoteBefore(CopySearch search, int firstRank, int lastRank)
            {
                for (int stepsBack = 1; ; stepsBack++)
                {
                    int firstIndexBack = search.SortedSuffixes[firstRank] - stepsBack;
                    if (firstIndexBack < 0 || Events[firstIndexBack] is not { CanPlayUnderShift: true } eventBack)
                    {
                        return stepsBack - 1;
                    }

                    for (int rank = firstRank + 1; rank <= lastRank; rank++)
                    {
                        int indexBack = search.SortedSuffixes[rank] - stepsBack;
                        if (indexBack < 0 || Events[indexBack]?.PitchlessKey != eventBack.PitchlessKey)
                        {
                            return stepsBack - 1;
                        }
                    }

                    if (eventBack.NoteValue is not null)
                    {
                        return stepsBack;
                    }
                }
            }

            // The most a phrase could save, every copy but the smallest counted whether or not it overlaps another.
            private int MostBytesSaved(Candidate candidate)
            {
                int totalBytes = 0;
                int smallestCopyBytes = int.MaxValue;

                foreach (int start in candidate.Starts())
                {
                    int copyBytes = CopyBytes(start, candidate.Length);
                    totalBytes += copyBytes;
                    smallestCopyBytes = Math.Min(smallestCopyBytes, copyBytes);
                }

                return totalBytes - smallestCopyBytes;
            }

            // The bytes one copy of a phrase takes written out where it stands.
            private int CopyBytes(int start, int length)
            {
                return _bytesBefore[start + length] - _bytesBefore[start];
            }

            // A phrase's copies taken left to right where they overlap neither each other nor a phrase already taken, each with its shift from the first,
            // a run of shifted copies kept only where it saves more than its call and transposes cost, and what writing them as label calls saves.
            private (List<PhraseCopy> Copies, int BytesSaved) Price(Candidate candidate)
            {
                int length = candidate.Length;
                var starts = candidate.Starts()
                    .Where(start => CanHaveEdgeAt(start) && CanHaveEdgeAt(start + length) && _claimedBefore[start + length] == _claimedBefore[start])
                    .Order()
                    .ToList();

                var keptStarts = new List<int>();
                foreach (int start in starts)
                {
                    if (keptStarts.Count == 0 || start >= keptStarts[^1] + length)
                    {
                        keptStarts.Add(start);
                    }
                }

                if (keptStarts.Count < 2)
                {
                    return ([], 0);
                }

                var copies = keptStarts.Select(start => new PhraseCopy(start, ShiftBetween(keptStarts[0], start, length))).ToList();
                var paidRuns = RunsOfAdjacentCopies(copies, length)
                    .Where(run => run.Shift == NoShift || run.Copies.Sum(copy => CopyBytes(copy.Start, length)) > CallSiteBytes(run.Shift))
                    .ToList();
                var paidCopies = paidRuns.SelectMany(run => run.Copies).ToList();

                if (paidCopies.Count < 2)
                {
                    return (paidCopies, 0);
                }

                int bytesSaved = paidCopies.Skip(1).Sum(copy => CopyBytes(copy.Start, length)) - LabelDefinitionBytes - paidRuns.Sum(run => CallSiteBytes(run.Shift));

                return (paidCopies, bytesSaved);
            }

            // The semitones a copy plays shifted from the first: the step between their first notes, none where the phrase holds no note.
            private int ShiftBetween(int firstCopyStart, int copyStart, int length)
            {
                for (int offset = 0; offset < length; offset++)
                {
                    if (Events[firstCopyStart + offset]!.NoteValue is int firstNoteValue)
                    {
                        return Events[copyStart + offset]!.NoteValue!.Value - firstNoteValue;
                    }
                }

                return 0;
            }

            // Whether a loop edge may stand in front of a position, a segment's end always allowing one.
            private bool CanHaveEdgeAt(int position)
            {
                return Events[position] is not { } phraseEvent || phraseEvent.CanHaveEdgeBefore;
            }

            // Marks a taken phrase's copies so no later phrase overlaps them.
            private void Claim(List<PhraseCopy> copies, int length)
            {
                foreach (var copy in copies)
                {
                    for (int position = copy.Start; position < copy.Start + length; position++)
                    {
                        _isClaimed[position] = true;
                    }
                }

                for (int position = 0; position < _isClaimed.Length; position++)
                {
                    _claimedBefore[position + 1] = _claimedBefore[position] + (_isClaimed[position] ? 1 : 0);
                }
            }

            // One way of matching copies: each event's id, and the suffixes of the ids in order.
            private sealed class CopySearch
            {
                public readonly List<int> Ids;
                public readonly int[] SortedSuffixes;

                // Sorts the suffixes of the ids.
                public CopySearch(List<int> ids)
                {
                    Ids = ids;
                    SortedSuffixes = SuffixArray(ids);
                }
            }

            // A repeat to price: the suffixes sharing it in the search that found it, how far its copies reach back in front of the part they share, and
            // its length in events.
            private sealed record Candidate(CopySearch Search, int FirstRank, int LastRank, int StepsBack, int Length)
            {
                // Where the copy of the first suffix starts.
                public int FirstStart
                {
                    get
                    {
                        return Search.SortedSuffixes[FirstRank] - StepsBack;
                    }
                }

                // Where each copy starts, in suffix order.
                public IEnumerable<int> Starts()
                {
                    return Enumerable.Range(FirstRank, LastRank - FirstRank + 1).Select(rank => Search.SortedSuffixes[rank] - StepsBack);
                }
            }
        }
    }
}
